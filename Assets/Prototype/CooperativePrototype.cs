using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

// Desktop diagnostics prototype. XR rig and device SDK are separate integration steps.
public class CooperativePrototype : MonoBehaviour
{
    [Serializable] public class SensorEvent { public int seq; public string action; public float value; }
    [Serializable] public class AiRequest { public string action; public float value; public int progress; }
    [Serializable] public class AiReply { public string reply; }
    public int udpPort = 8765;
    public string aiEndpoint = "http://127.0.0.1:8787/respond";
    public bool useAiService;
    readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
    UdpClient receiver;
    Thread worker;
    volatile bool running;
    string status = "No hardware input yet";
    string reply = "Use the simulated controls or send an ESP32 event.";
    int progress, lastSeq = -1;
    float dial;
    bool held, calibrated, completed;
    float heldSince;
    float lastHardwareTime = -1;
    Renderer beacon;
    Coroutine aiJob;

    void Start()
    {
        BuildRoom();
        try {
            receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, udpPort));
            receiver.Client.ReceiveTimeout = 250;
            running = true;
            worker = new Thread(ReadPackets) { IsBackground = true };
            worker.Start();
            status = "Listening on localhost UDP " + udpPort;
        } catch (Exception e) { status = "UDP unavailable: " + e.Message; }
    }
    void ReadPackets()
    {
        while (running) {
            try { IPEndPoint source = null; var bytes = receiver.Receive(ref source);
                if (bytes.Length <= 2048 && inbox.Count < 128) inbox.Enqueue(Encoding.UTF8.GetString(bytes));
            } catch (SocketException) { } catch (ObjectDisposedException) { break; }
        }
    }
    void Update()
    {
        int count = 0;
        while (count++ < 32 && inbox.TryDequeue(out var json)) {
            try { var e = JsonUtility.FromJson<SensorEvent>(json);
                if (e == null || e.seq <= lastSeq || !Valid(e)) continue;
                lastSeq = e.seq; lastHardwareTime = Time.realtimeSinceStartup; Apply(e.action, e.value);
                status = "Hardware event " + e.seq + ": " + e.action;
            } catch (Exception) { status = "Malformed sensor packet ignored"; }
        }
        if (lastHardwareTime >= 0 && Time.realtimeSinceStartup - lastHardwareTime > .5f) {
            held = false; status = "Sensor stream stale; switch released";
        }
        if (held && !completed && calibrated && Time.realtimeSinceStartup - heldSince >= 2f) {
            completed = true; progress = 2;
            beacon.material.color = Color.green;
            Respond("stabilized", 1);
        }
    }
    bool Valid(SensorEvent e) => !float.IsNaN(e.value) && !float.IsInfinity(e.value)
        && (e.action == "dial" || e.action == "hold" || e.action == "release");
    void Apply(string action, float value)
    {
        if (completed) return;
        bool previousCalibration = calibrated;
        if (action == "dial") {
            dial = Mathf.Clamp01(value);
            calibrated = dial >= .45f && dial <= .55f;
            progress = calibrated ? 1 : 0;
            beacon.material.color = calibrated ? Color.cyan : Color.red;
            if (!calibrated) heldSince = Time.realtimeSinceStartup;
        }
        if (action == "hold" && !held) { held = true; heldSince = Time.realtimeSinceStartup; }
        if (action == "release") held = false;
        if (action != "dial" || previousCalibration != calibrated) Respond(action, value);
    }
    void Respond(string action, float value)
    {
        reply = completed ? "Together we stabilized the signal. Task complete."
            : calibrated ? "The signal is aligned. Hold the switch for two seconds."
            : "Turn the dial toward its center; I will watch the signal.";
        if (!useAiService) return;
        if (aiJob != null) StopCoroutine(aiJob);
        aiJob = StartCoroutine(RequestAi(new AiRequest { action = action, value = value, progress = progress }));
    }
    IEnumerator RequestAi(AiRequest state)
    {
        using (var request = new UnityWebRequest(aiEndpoint, "POST")) {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(state)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json"); request.timeout = 12;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) {
                AiReply result = null;
                try { result = JsonUtility.FromJson<AiReply>(request.downloadHandler.text); } catch (Exception) { }
                if (result != null && !string.IsNullOrWhiteSpace(result.reply)) reply = result.reply;
                else status = "AI response invalid; local guidance retained";
            } else status = "AI unavailable; local guidance retained";
        }
        aiJob = null;
    }
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(20,20,480,360), GUI.skin.box);
        GUILayout.Label("COOPERATIVE SIGNAL / desktop integration prototype");
        GUILayout.Label(status); GUILayout.Label("Progress: " + progress + "/2");
        GUILayout.Label("Dial: " + dial.ToString("F2"));
        var value = GUILayout.HorizontalSlider(dial, 0, 1);
        if (Mathf.Abs(value - dial) > .005f) Apply("dial", value);
        if (GUILayout.Button(held ? "Release switch" : "Hold switch")) Apply(held ? "release" : "hold", 1);
        GUILayout.Space(15); GUILayout.Label("Partner: " + reply);
        GUILayout.Label(useAiService ? "AI service enabled" : "Local scripted partner (no live AI)");
        GUILayout.EndArea();
    }
    void BuildRoom()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Work platform"; floor.transform.position = new Vector3(0,-.1f,0);
        floor.transform.localScale = new Vector3(6,.2f,6);
        var signal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        signal.name = "Signal beacon"; signal.transform.position = new Vector3(0,1,1);
        beacon = signal.GetComponent<Renderer>(); beacon.material.color = Color.red;
        var partner = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        partner.name = "Partner placeholder"; partner.transform.position = new Vector3(1.4f,1,1);
        partner.GetComponent<Renderer>().material.color = new Color(.3f,.5f,.9f);
        if (Camera.main != null) { Camera.main.transform.position = new Vector3(0,1.6f,-3);
            Camera.main.transform.LookAt(new Vector3(0,1,1)); }
    }
    void OnDestroy()
    {
        running = false; receiver?.Close(); worker?.Join(600);
    }
}
