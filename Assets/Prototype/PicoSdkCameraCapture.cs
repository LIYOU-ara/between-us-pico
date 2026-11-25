using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.PXR;

// Runs on the headset with the PICO Android loader. PC Editor receives frames via ADB.
// The HTTP server binds to loopback only and never uploads camera images to AI services.
public class PicoSdkCameraCapture : MonoBehaviour
{
    [Serializable] public class CameraStatus {
        public string source="pico_sdk_camera", status="starting", model, system;
        public int camera_id, width, height, frames;
        public long capture_timestamp_ns, frame_unix_ms;
        public bool calibrated, extrinsics_valid, application_focused;
        public float fx, fy, cx, cy;
        public float[] camera_position, camera_rotation, camera_rotation_sdk, head_position, head_rotation;
        public bool texture_rows_reversed;
    }
    sealed class Frame {
        public byte[] jpeg;
        public string metadata;
        public long ticks;
    }
    public TextMesh display;
    public Transform headCamera, markerFrame, marker;
    public float markerTimeout=.5f;
    volatile string pendingPose;
    readonly Dictionary<long,HeadPose> capturePoses=new Dictionary<long,HeadPose>();
    readonly Queue<long> poseOrder=new Queue<long>();
    struct HeadPose { public Vector3 position; public Quaternion rotation; }
    float lastMarker=-100;
    long acceptedCapture=-1;
    public Renderer previewRenderer;
    public XrCameraIdPICO cameraId=XrCameraIdPICO.XR_CAMERA_ID_RGB_LEFT_PICO;
    public int preferredWidth=640, preferredHeight=480;
    public bool flipRowsForTexture=false;
    CameraStatus state=new CameraStatus();
    volatile string snapshot="{\"source\":\"pico_sdk_camera\",\"status\":\"starting\",\"frames\":0}";
    volatile Frame current;
    TcpListener listener;
    Thread server;
    Texture2D texture;
    byte[] pixels, packed;
    CancellationTokenSource cancellation=new CancellationTokenSource();
    long lastCapture;
    float nextFrame;
    float nextEncodedFrame;
    bool ready, deviceCreated, sessionCreated, running=true;
    XrCameraIntrinsics intrinsics;
    XrCameraExtrinsics extrinsics;
    bool intrinsicsValid, extrinsicsValid;
    Matrix4x4 glassWorldToOptical;
    float glassFrameAt = -100;
    bool glassPoseValid;

    // Local material sampling only. Reuse the existing RGBA upload; no HTTP,
    // JPEG decoding or additional camera session is needed by the glass UI.
    public bool TryGetGlassFrame(out Texture2D image, out Matrix4x4 worldToOptical,
        out Vector4 normalizedIntrinsics, out bool rowsReversed, out int frameId)
    {
        image = texture;
        worldToOptical = glassWorldToOptical;
        normalizedIntrinsics = new Vector4(state.fx / Mathf.Max(1, state.width), state.fy / Mathf.Max(1, state.height),
            state.cx / Mathf.Max(1, state.width), state.cy / Mathf.Max(1, state.height));
        rowsReversed = flipRowsForTexture;
        frameId = state.frames;
        return ready && glassPoseValid && image != null && Application.isFocused &&
            Time.realtimeSinceStartup - glassFrameAt < .3f;
    }
    public float GlassFrameAgeMs => Mathf.Max(0, (Time.realtimeSinceStartup - glassFrameAt) * 1000);

    IEnumerator Start() {
        state.model=SystemInfo.deviceModel;
        state.system=SystemInfo.operatingSystem;
        state.camera_id=(int)cameraId;
        listener=new TcpListener(IPAddress.Loopback,8890);
        try { listener.Start(); server=new Thread(Serve){IsBackground=true}; server.Start(); }
        catch(Exception e) { SetStatus("HTTP: "+e.Message); yield break; }
#if UNITY_ANDROID && !UNITY_EDITOR
        if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera)) {
            SetStatus("Please allow camera permission in the headset");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            float deadline=Time.realtimeSinceStartup+60;
            while(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera)
                && Time.realtimeSinceStartup<deadline) yield return null;
        }
        if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera)) {
            SetStatus("Camera permission was not granted"); yield break;
        }
        // Let the XR runtime enter its first session before requesting camera capabilities.
        while(!Application.isFocused) yield return null;
        yield return new WaitForSecondsRealtime(2);
        var task=InitializeCamera();
        while(!task.IsCompleted) yield return null;
        if(task.IsFaulted) SetStatus("Camera initialization: "+task.Exception.GetBaseException().Message);
#else
        SetStatus("Run this scene on PICO 4 Ultra, OS 5.15+; Editor cannot capture headset camera pixels");
#endif
    }
    async Task InitializeCamera() {
        SetStatus("Enumerating PICO cameras");
        PxrResult result=PXR_CameraImage.GetAvailableCameras(out var cameras);
        Require(result,"GetAvailableCameras");
        if(cameras==null || cameras.Length==0) throw new InvalidOperationException("Runtime exposed no cameras");
        if(Array.IndexOf(cameras,cameraId)<0) cameraId=cameras[0];
        state.camera_id=(int)cameraId;
        Require(PXR_CameraImage.GetCameraImageResolutionCapability(cameraId,out var sizes),"Get resolutions");
        if(sizes==null || sizes.Length==0) throw new InvalidOperationException("No supported resolutions");
        var size=sizes[0];
        foreach(var candidate in sizes)
            if(Math.Abs(candidate.width*candidate.height-preferredWidth*preferredHeight)
                <Math.Abs(size.width*size.height-preferredWidth*preferredHeight)) size=candidate;
        state.width=size.width; state.height=size.height;
        Require(PXR_CameraImage.GetCameraImageFpsCapability(cameraId,out var fpsValues),"Get frame rates");
        var fps=XrCameraImageFpsPICO.XR_CAMERA_IMAGE_FPS_30_PICO;
        if(fpsValues==null || fpsValues.Length==0) throw new InvalidOperationException("No supported frame rates");
        if(Array.IndexOf(fpsValues,fps)<0) fps=fpsValues[0];
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token)) {
            timeout.CancelAfter(20000);
            SetStatus("Creating camera device");
            Require(await PXR_CameraImage.CreateCameraDeviceAsync(cameraId,timeout.Token),"Create device");
            deviceCreated=true;
            SetStatus("Creating RGBA capture session");
            Require(await PXR_CameraImage.CreateCameraCaptureSessionAsync(cameraId,size.width,size.height,fps,
                XrCameraImageFormatPICO.XR_CAMERA_IMAGE_FORMAT_RGBA_8888_PICO,
                XrCameraDataTransferTypePICO.XR_CAMERA_DATA_TRANSFER_TYPE_RAW_BUFFER_PICO,
                XrCameraModelPICO.XR_CAMERA_MODEL_PINHOLE_PICO,timeout.Token),"Create capture session");
            sessionCreated=true;
        }
        intrinsicsValid=PXR_CameraImage.GetCameraIntrinsics(cameraId,out intrinsics)==PxrResult.SUCCESS;
        extrinsicsValid=PXR_CameraImage.GetCameraExtrinsics(cameraId,out extrinsics)==PxrResult.SUCCESS;
        if(intrinsicsValid) {
            state.fx=intrinsics.focalLength.X; state.fy=intrinsics.focalLength.Y;
            state.cx=intrinsics.principalPoint.X; state.cy=intrinsics.principalPoint.Y;
        }
        if(extrinsicsValid) {
            state.camera_position=new[]{extrinsics.pose.Position.X,extrinsics.pose.Position.Y,extrinsics.pose.Position.Z};
            state.camera_rotation_sdk=new[]{extrinsics.pose.Orientation.X,extrinsics.pose.Orientation.Y,extrinsics.pose.Orientation.Z,extrinsics.pose.Orientation.W};
            Quaternion optical=OpticalCameraRotation();
            state.camera_rotation=new[]{optical.x,optical.y,optical.z,optical.w};
        }
        Require(PXR_CameraImage.BeginCameraCapture(cameraId),"Begin capture");
        ready=true; SetStatus("Capture started; waiting for a real frame");
    }
    static void Require(PxrResult result,string operation) {
        if(result!=PxrResult.SUCCESS) throw new InvalidOperationException(operation+": "+result);
    }
    void SetStatus(string value) {
        state.status=value; snapshot=JsonUtility.ToJson(state);
        Debug.Log("PICO_SDK_CAMERA: "+value);
    }
    void Update() {
        UpdateHeadAndMarker();
        if(display!=null) display.text="PICO SDK CAMERA\n"+state.status+"\nFrames: "+state.frames+" / "+state.width+" x "+state.height+"\nTag 22: "+(marker!=null && marker.gameObject.activeSelf ? "visible" : "waiting");
        if(!ready || Time.realtimeSinceStartup<nextFrame) return;
        // Glass needs frequent local uploads; JPEG remains at the original 10 Hz
        // for face detection and the loopback inspector.
        nextFrame=Time.realtimeSinceStartup+1f/30;
        PxrResult acquired=PXR_CameraImage.AcquireCameraImage(cameraId,lastCapture,out ulong id,out long captured);
        if(acquired!=PxrResult.SUCCESS) return; // No new image is a normal runtime result.
        lastCapture=captured;
        try {
            Require(PXR_CameraImage.GetCameraImageData(cameraId,id,out var raw),"Get frame buffer");
            int width=checked((int)raw.width), height=checked((int)raw.height);
            int stride=checked((int)raw.stride), pixelStride=checked((int)raw.pixelStride);
            if(raw.buffer==IntPtr.Zero || raw.bytesPerPixel!=4 || width<=0 || height<=0 || width>4096 || height>4096
                || pixelStride<4 || stride<(width-1)*pixelStride+4 || raw.bufferSize<(long)(height-1)*stride+(width-1)*pixelStride+4)
                throw new InvalidOperationException("Invalid RGBA image layout");
            int count=checked((height-1)*stride+(width-1)*pixelStride+4);
            if(pixels==null || pixels.Length!=count) pixels=new byte[count];
            // Only copy bytes declared by the runtime; the final row may have no trailing padding.
            Marshal.Copy(raw.buffer,pixels,0,count);
            if(packed==null || packed.Length!=width*height*4) packed=new byte[width*height*4];
            for(int y=0;y<height;y++) {
                int destination=(flipRowsForTexture?height-1-y:y)*width*4;
                if(pixelStride==4) Buffer.BlockCopy(pixels,y*stride,packed,destination,width*4);
                else for(int x=0;x<width;x++) Buffer.BlockCopy(pixels,y*stride+x*pixelStride,packed,destination+x*4,4);
            }
            if(texture==null || texture.width!=width || texture.height!=height) {
                if(texture!=null) Destroy(texture);
                texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
                if(previewRenderer!=null) previewRenderer.material.mainTexture=texture;
            }
            texture.LoadRawTextureData(packed); texture.Apply(false);
            state.width=width; state.height=height; state.frames++; state.texture_rows_reversed=flipRowsForTexture;
            state.capture_timestamp_ns=captured; state.frame_unix_ms=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            state.application_focused=Application.isFocused;
            state.calibrated=intrinsicsValid && state.fx>0 && state.fy>0 && state.cx>=0 && state.cx<=width && state.cy>=0 && state.cy<=height;
            state.extrinsics_valid=extrinsicsValid;
            var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool hasPosition=head.TryGetFeatureValue(CommonUsages.devicePosition,out var p);
            bool hasRotation=head.TryGetFeatureValue(CommonUsages.deviceRotation,out var q);
            if(hasPosition) state.head_position=new[]{p.x,p.y,p.z};
            if(hasRotation) state.head_rotation=new[]{q.x,q.y,q.z,q.w};
            glassPoseValid=state.calibrated && extrinsicsValid && hasPosition && hasRotation && headCamera!=null;
            if(glassPoseValid) {
                var offset=new Vector3(extrinsics.pose.Position.X,extrinsics.pose.Position.Y,extrinsics.pose.Position.Z);
                var trackingToWorld=headCamera.parent!=null ? headCamera.parent.localToWorldMatrix : Matrix4x4.identity;
                glassWorldToOptical=(trackingToWorld*Matrix4x4.TRS(p+q*offset,q*OpticalCameraRotation(),Vector3.one)).inverse;
                glassFrameAt=Time.realtimeSinceStartup;
            }
            if(state.head_position!=null && state.head_rotation!=null) {
                var hp=state.head_position; var hq=state.head_rotation;
                capturePoses[captured]=new HeadPose { position=new Vector3(hp[0],hp[1],hp[2]), rotation=new Quaternion(hq[0],hq[1],hq[2],hq[3]) };
                poseOrder.Enqueue(captured);
                while(poseOrder.Count>64) capturePoses.Remove(poseOrder.Dequeue());
            }
            state.status="Receiving real SDK camera frames";
            string metadata=JsonUtility.ToJson(state); snapshot=metadata;
            if(current==null || Time.realtimeSinceStartup>=nextEncodedFrame) {
                nextEncodedFrame=Time.realtimeSinceStartup+.1f;
                current=new Frame { jpeg=texture.EncodeToJPG(80), metadata=metadata, ticks=System.Diagnostics.Stopwatch.GetTimestamp() };
            }
            if(state.frames==1) Debug.Log("PICO_SDK_CAMERA_FIRST_FRAME: "+metadata);
        } catch(Exception e) { ready=false; SetStatus("Frame error: "+e.Message); }
        finally { PXR_CameraImage.ReleaseCameraImage(cameraId,id); }
    }
    Quaternion OpticalCameraRotation() {
        // Camera-local API axes are computer-vision axes (y down, z forward).
        // SDK's generic OpenXR-to-Unity pose conversion assumes camera-local y up, z back.
        // Convert the local optical frame as well; measured Ultra SDK rotation is ~180 degrees around x.
        return new Quaternion(extrinsics.pose.Orientation.X,extrinsics.pose.Orientation.Y,
            extrinsics.pose.Orientation.Z,extrinsics.pose.Orientation.W)*Quaternion.Euler(180,0,0);
    }
    static bool ValidVector(float[] a) => a!=null && a.Length==3 && !float.IsNaN(a[0]+a[1]+a[2]) && !float.IsInfinity(a[0]+a[1]+a[2]);
    static Vector3 Vector(float[] a) => new Vector3(a[0],a[1],a[2]);
    void UpdateHeadAndMarker() {
        if(headCamera!=null) {
            var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if(head.TryGetFeatureValue(CommonUsages.devicePosition,out var p)) headCamera.localPosition=p;
            if(head.TryGetFeatureValue(CommonUsages.deviceRotation,out var q)) headCamera.localRotation=q;
        }
        string json=Interlocked.Exchange(ref pendingPose,null);
        if(json!=null && marker!=null && markerFrame!=null) {
            try {
                var pose=JsonUtility.FromJson<AprilTagProbe.TagPacket>(json);
                bool valid=pose!=null && pose.source=="pico_sdk_camera" && pose.family=="tag36h11" && pose.id==22
                    && pose.tracked && pose.capture_age_ms>=0 && pose.capture_age_ms<500
                    && ValidVector(pose.position) && ValidVector(pose.forward) && ValidVector(pose.up) && pose.position[2]>0
                    && Vector3.Cross(Vector(pose.forward),Vector(pose.up)).sqrMagnitude>.1f;
                if(!valid) { marker.gameObject.SetActive(false); lastMarker=-100; }
                else if(pose.capture_timestamp_ns!=acceptedCapture && capturePoses.TryGetValue(pose.capture_timestamp_ns,out var headPose)) {
                    acceptedCapture=pose.capture_timestamp_ns;
                    Vector3 offset=extrinsicsValid ? new Vector3(extrinsics.pose.Position.X,extrinsics.pose.Position.Y,extrinsics.pose.Position.Z) : Vector3.zero;
                    Quaternion rotation=extrinsicsValid ? OpticalCameraRotation() : Quaternion.identity;
                    markerFrame.localPosition=headPose.position+headPose.rotation*offset;
                    markerFrame.localRotation=headPose.rotation*rotation;
                    marker.localPosition=Vector(pose.position); marker.localRotation=Quaternion.LookRotation(Vector(pose.forward),Vector(pose.up));
                    lastMarker=Time.realtimeSinceStartup; marker.gameObject.SetActive(true);
                }
            } catch(Exception e) { Debug.LogWarning("ULTRA_MARKER_PACKET: "+e.Message); marker.gameObject.SetActive(false); }
        }
        if(marker!=null && Time.realtimeSinceStartup-lastMarker>markerTimeout) marker.gameObject.SetActive(false);
    }
    void Serve() {
        while(running) {
            try {
                using(var client=listener.AcceptTcpClient()) {
                    client.ReceiveTimeout=2000; client.SendTimeout=2000;
                    var stream=client.GetStream();
                    var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                    string first=reader.ReadLine(); if(first==null) continue;
                    string line; int contentLength=0;
                    while((line=reader.ReadLine())!=null && line.Length>0) {
                        if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))
                            int.TryParse(line.Substring(15).Trim(),out contentLength);
                    }
                    if(first.StartsWith("POST /pose ")) {
                        if(contentLength<=0 || contentLength>65536) continue;
                        char[] body=new char[contentLength]; int offset=0;
                        while(offset<contentLength) {
                            int count=reader.Read(body,offset,contentLength-offset);
                            if(count<=0) break; offset+=count;
                        }
                        if(offset==contentLength) pendingPose=new string(body);
                        byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                        stream.Write(response,0,response.Length); continue;
                    }
                    Frame frame=current;
                    bool wantsFrame=first.StartsWith("GET /frame ");
                    byte[] data=wantsFrame && frame!=null ? frame.jpeg : Encoding.UTF8.GetBytes(snapshot);
                    int code=wantsFrame && frame==null ? 503 : 200;
                    string extra="";
                    if(wantsFrame && frame!=null) {
                        double age=(System.Diagnostics.Stopwatch.GetTimestamp()-frame.ticks)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                        extra="X-Camera-Meta: "+Convert.ToBase64String(Encoding.UTF8.GetBytes(frame.metadata))+"\r\nX-Frame-Age-Ms: "+
                            age.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"\r\n";
                    }
                    string header="HTTP/1.1 "+code+" OK\r\nConnection: close\r\nCache-Control: no-store\r\nContent-Type: "+
                        (wantsFrame && frame!=null ? "image/jpeg" : "application/json")+"\r\nContent-Length: "+data.Length+"\r\n"+extra+"\r\n";
                    byte[] bytes=Encoding.ASCII.GetBytes(header); stream.Write(bytes,0,bytes.Length); stream.Write(data,0,data.Length);
                }
            } catch(Exception) { if(!running) break; }
        }
    }
    void OnDestroy() {
        running=false; ready=false; cancellation.Cancel(); listener?.Stop();
#if UNITY_ANDROID && !UNITY_EDITOR
        if(sessionCreated) { PXR_CameraImage.EndCameraCapture(cameraId); PXR_CameraImage.DestroyCameraCaptureSession(cameraId); }
        if(deviceCreated) PXR_CameraImage.DestroyCameraDevice(cameraId);
#endif
        if(texture!=null) Destroy(texture);
    }
}
