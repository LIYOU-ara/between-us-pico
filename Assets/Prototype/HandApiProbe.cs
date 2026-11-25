using System;
using System.Collections;
using System.Text;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;
using Unity.XR.PXR;
#if UNITY_EDITOR
using HandJointLocations = Unity.XR.PICO.LivePreview.HandJointLocations;
using HandJointLocation = Unity.XR.PICO.LivePreview.HandJointLocation;
using HandLocationStatus = Unity.XR.PICO.LivePreview.HandLocationStatus;
#endif

// A pinch edge triggers one request. Tracking loss always clears the pinch state.
public class HandApiProbe : MonoBehaviour
{
    [Serializable] class RequestState { public string action = "pinch"; public int progress; public float value = 1; }
    [Serializable] class Reply { public string reply; public string source; public int elapsed_ms; }
    public string endpoint = "http://127.0.0.1:8787/respond";
    public TextMesh display;
    public Transform trackingOrigin;
    public Renderer target;
    public bool busy;
    readonly HandJointLocations[] joints = {
        new HandJointLocations { jointLocations = new HandJointLocation[26] },
        new HandJointLocations { jointLocations = new HandJointLocation[26] }
    };
    readonly bool[] pinched = new bool[2];
    readonly Transform[,] dots = new Transform[2,26];
    string answer = "Pinch thumb and index finger to call the local AI bridge.";
    string handStatus = "Waiting for hand tracking";
    string apiStatus = "No API request yet";
    int calls;
    float lastRequest = -10;
    Font font;
    bool panelPlaced;
    float lastStatus;
    [Serializable] class ProbeStatus { public int trackedHands; public bool pinching; public bool busy; public int requests; public string api; public string reply; }

    void Start()
    {
        font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 36);
        if (display != null && font != null) { display.font = font; display.GetComponent<Renderer>().material = font.material; }
        for (int hand=0; hand<2; hand++) for (int joint=0; joint<26; joint++) {
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Hand " + hand + " joint " + joint;
            Destroy(dot.GetComponent<Collider>());
            dot.transform.SetParent(trackingOrigin, false);
            dot.transform.localScale = Vector3.one * .009f;
            dot.GetComponent<Renderer>().material.color = hand == 0 ? Color.cyan : Color.yellow;
            dots[hand,joint] = dot.transform; dot.SetActive(false);
        }
    }
    void Update()
    {
        var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (Camera.main != null) {
            if (head.TryGetFeatureValue(CommonUsages.devicePosition,out var position)) {
                Camera.main.transform.localPosition = position;
                if (!panelPlaced && display != null) {
                    display.transform.position = trackingOrigin.TransformPoint(position + new Vector3(-.65f,.3f,1.7f));
                    if (target != null) target.transform.position = trackingOrigin.TransformPoint(position + new Vector3(0,-.3f,1.5f));
                    panelPlaced = true;
                }
            }
            if (head.TryGetFeatureValue(CommonUsages.deviceRotation,out var rotation)) Camera.main.transform.localRotation = rotation;
        }
        int activeHands = 0;
        for (int hand=0; hand<2; hand++) {
#if UNITY_EDITOR
            Unity.XR.PICO.LivePreview.PXR_PTApi.UPxr_GetHandTrackerJointLocations(hand,ref joints[hand]);
            bool tracked = joints[hand].isActive != 0 && joints[hand].jointLocations != null && joints[hand].jointCount >= 26;
#else
            bool tracked = PXR_HandTracking.GetJointLocations((HandType)hand,ref joints[hand])
                && joints[hand].isActive != 0 && joints[hand].jointLocations != null && joints[hand].jointCount >= 26;
#endif
            if (tracked) activeHands++;
            for (int joint=0; joint<26; joint++) {
                bool valid = tracked && (joints[hand].jointLocations[joint].locationStatus & HandLocationStatus.PositionValid) != 0;
                dots[hand,joint].gameObject.SetActive(valid);
                if (valid) dots[hand,joint].localPosition = joints[hand].jointLocations[joint].pose.Position.ToVector3();
            }
            bool validTips = tracked && dots[hand,5].gameObject.activeSelf && dots[hand,10].gameObject.activeSelf;
            bool pinch = validTips && Vector3.Distance(dots[hand,5].localPosition,dots[hand,10].localPosition) < (pinched[hand] ? .035f : .022f);
            if (pinch && !pinched[hand]) Trigger();
            pinched[hand] = pinch;
        }
        handStatus = "Tracked hands: " + activeHands + " / Pinch: " + (pinched[0] || pinched[1]);
        if (Time.realtimeSinceStartup - lastStatus > 1) {
            lastStatus = Time.realtimeSinceStartup;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),"hand-api-status.json"),
                JsonUtility.ToJson(new ProbeStatus { trackedHands = activeHands, pinching = pinched[0] || pinched[1], busy = busy, requests = calls, api = apiStatus, reply = answer },true));
        }
        if (display != null) {
            string text = "PICO 4 / HAND + API TEST\n" + handStatus + "\n" + apiStatus + "\n" + answer;
            if (font != null) font.RequestCharactersInTexture(text,36);
            display.text = text;
        }
    }
    public void Trigger()
    {
        if (busy || Time.realtimeSinceStartup - lastRequest < 2) return;
        lastRequest = Time.realtimeSinceStartup;
        StartCoroutine(Call());
    }
    IEnumerator Call()
    {
        busy = true; calls++; apiStatus = "Request " + calls + " pending";
        if (target != null) target.material.color = Color.yellow;
        using (var request = new UnityWebRequest(endpoint,"POST")) {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new RequestState())));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type","application/json"); request.timeout = 35;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) {
                Reply response = null;
                try { response = JsonUtility.FromJson<Reply>(request.downloadHandler.text); } catch (Exception) { }
                if (response != null && !string.IsNullOrWhiteSpace(response.reply)) {
                    answer = response.reply;
                    apiStatus = "Request " + calls + " OK / " + response.source + " / " + response.elapsed_ms + " ms";
                    if (target != null) target.material.color = Color.green;
                    Debug.Log("HAND_API_SUCCESS source=" + response.source + " latency_ms=" + response.elapsed_ms);
                } else { apiStatus = "Invalid response"; if (target != null) target.material.color = Color.red; }
            } else { apiStatus = "API failed: HTTP " + request.responseCode; if (target != null) target.material.color = Color.red; }
        }
        busy = false;
    }
    void OnGUI() { if (GUI.Button(new Rect(20,20,200,40),"API test (desktop)")) Trigger(); }
}
