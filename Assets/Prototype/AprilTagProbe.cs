using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;

// Camera2 frames -> AprilTagBridge.py -> this receiver. No pose is shown without a valid packet.
public class AprilTagProbe : MonoBehaviour
{
    [Serializable] public class TagPacket {
        public string source, status, family;
        public int id, image_width, image_height, frames;
        public bool tracked, calibrated, extrinsics_valid;
        public long timestamp_ms, capture_timestamp_ns;
        public float capture_age_ms, tag_size_m, fx, fy, cx, cy, reprojection_px;
        public float[] position, forward, up, camera_position, camera_rotation;
    }
    public Transform marker, captureFrame, preview;
    public TextMesh display;
    public int port = 8891;
    public bool showCameraPreview = true;
    public Vector3 cameraOffsetFromHead;
    public Vector3 cameraEulerFromHead;
    public float lostTimeout = .5f;
    UdpClient receiver;
    TagPacket packet;
    float lastPacket = -100, lastReport;
    Texture2D image;
    bool placed;
    Font font;
    string error;
    struct HeadSample { public double time; public Vector3 position; public Quaternion rotation; }
    readonly List<HeadSample> history = new List<HeadSample>();
    static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0;
    static Vector3 V(float[] a) => new Vector3(a[0],a[1],a[2]);
    static bool Valid(float[] a) => a != null && a.Length == 3 && !float.IsNaN(a[0]+a[1]+a[2]) && !float.IsInfinity(a[0]+a[1]+a[2]);
    void Start() {
        marker.gameObject.SetActive(false); preview.gameObject.SetActive(false);
        font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei",36);
        if (font != null) { display.font=font; display.GetComponent<Renderer>().material=font.material; }
        try { receiver=new UdpClient(new IPEndPoint(IPAddress.Loopback,port)); }
        catch(Exception e) { error=e.Message; Debug.LogError("APRILTAG_RECEIVER: "+error); }
        StartCoroutine(RefreshPreview());
    }
    void Update() {
        var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
        var camera=Camera.main.transform;
        if(head.TryGetFeatureValue(CommonUsages.devicePosition,out var p)) camera.localPosition=p;
        if(head.TryGetFeatureValue(CommonUsages.deviceRotation,out var q)) camera.localRotation=q;
        history.Add(new HeadSample { time=Now, position=camera.position, rotation=camera.rotation });
        while(history.Count>1 && history[0].time < Now-2) history.RemoveAt(0);
        if(receiver!=null) {
            try {
                // Drain a bounded batch and apply only the latest packet, on Unity's main thread.
                for(int n=0;n<32 && receiver.Available>0;n++) {
                    IPEndPoint endpoint=null;
                    var candidate=JsonUtility.FromJson<TagPacket>(Encoding.UTF8.GetString(receiver.Receive(ref endpoint)));
                    if(candidate!=null && candidate.family=="tag36h11" && candidate.id==22 && candidate.timestamp_ms>0
                        && Math.Abs(Now-candidate.timestamp_ms/1000.0)<1) {
                        packet=candidate; lastPacket=Time.realtimeSinceStartup;
                    }
                }
            } catch(Exception e) { error=e.Message; }
        }
        bool fresh=packet!=null && Time.realtimeSinceStartup-lastPacket<lostTimeout;
        bool detected=fresh && packet.tracked && Valid(packet.position) && Valid(packet.forward) && Valid(packet.up)
            && packet.position[2]>0 && V(packet.forward).sqrMagnitude>.5f && V(packet.up).sqrMagnitude>.5f;
        if(fresh) {
            if(packet.source=="pico_camera2" || packet.source=="pico_sdk_camera") {
                double requested=packet.timestamp_ms/1000.0-packet.capture_age_ms/1000.0;
                HeadSample sample=history[history.Count-1];
                foreach(var h in history) if(Math.Abs(h.time-requested)<Math.Abs(sample.time-requested)) sample=h;
                Vector3 offset=cameraOffsetFromHead;
                Quaternion rotation=Quaternion.Euler(cameraEulerFromHead);
                if(packet.source=="pico_sdk_camera" && packet.extrinsics_valid && Valid(packet.camera_position)
                    && packet.camera_rotation!=null && packet.camera_rotation.Length==4) {
                    var r=packet.camera_rotation;
                    var candidate=new Quaternion(r[0],r[1],r[2],r[3]);
                    float norm=Quaternion.Dot(candidate,candidate);
                    if(!float.IsNaN(norm) && !float.IsInfinity(norm) && norm>.5f && norm<1.5f) {
                        offset=V(packet.camera_position); rotation=candidate.normalized;
                    }
                }
                captureFrame.SetPositionAndRotation(sample.position+sample.rotation*offset,sample.rotation*rotation);
            } else if(!placed) {
                // Still image/PC camera use a fixed diagnostic frame, never pretend to be head-mounted.
                captureFrame.SetPositionAndRotation(camera.position,camera.rotation); placed=true;
            }
        }
        marker.gameObject.SetActive(detected);
        if(detected) {
            marker.localPosition=V(packet.position);
            marker.localRotation=Quaternion.LookRotation(V(packet.forward),V(packet.up));
        }
        preview.gameObject.SetActive(fresh && showCameraPreview && image!=null);
        if(fresh && packet.fx>0 && packet.fy>0) {
            const float z=2;
            preview.localPosition=new Vector3((packet.image_width/2f-packet.cx)/packet.fx*z,
                -(packet.image_height/2f-packet.cy)/packet.fy*z,z);
            preview.localScale=new Vector3(packet.image_width/packet.fx*z,packet.image_height/packet.fy*z,1);
        }
        string text="APRILTAG 36h11 / ID 22\n"+(fresh ? packet.source+"\n"+packet.status : "Waiting for AprilTagBridge on port 8891");
        if(fresh) text+="\nFrames: "+packet.frames+" / Size: "+(packet.tag_size_m*100).ToString("F1")+" cm\n"+
            (packet.calibrated ? "Camera calibrated" : "Approximate FoV / depth; calibration required");
        if(!string.IsNullOrEmpty(error)) text+="\n"+error;
        if(font!=null) font.RequestCharactersInTexture(text,36);
        display.text=text;
        if(Time.realtimeSinceStartup-lastReport>1) {
            lastReport=Time.realtimeSinceStartup;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"AprilTag","unity-status.json"),
                JsonUtility.ToJson(new TagPacket { source=packet?.source ?? "none", status=text, tracked=detected,
                    id=22, family="tag36h11", frames=packet?.frames ?? 0, calibrated=packet?.calibrated ?? false },true));
        }
    }
    IEnumerator RefreshPreview() {
        while(true) {
            if(showCameraPreview) {
                using(var request=UnityWebRequestTexture.GetTexture("http://127.0.0.1:8892/frame")) {
                    request.timeout=2;
                    yield return request.SendWebRequest();
                    if(request.result==UnityWebRequest.Result.Success) {
                        if(image!=null) Destroy(image);
                        image=DownloadHandlerTexture.GetContent(request);
                        preview.GetComponent<Renderer>().material.mainTexture=image;
                    } else {
                        if(image!=null) Destroy(image);
                        image=null;
                    }
                }
            }
            yield return new WaitForSecondsRealtime(.2f);
        }
    }
    void OnDestroy() { receiver?.Close(); if(image!=null) Destroy(image); }
}
