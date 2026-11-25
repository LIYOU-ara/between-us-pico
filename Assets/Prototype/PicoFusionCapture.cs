using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;

// Native-headset microphone. One bounded utterance at a time; no stored recordings.
public class PicoFusionCapture : MonoBehaviour {
    public TextMesh statusText, subtitleText;
    public string endpoint="http://127.0.0.1:8795/transcribe";
    public float voiceThreshold=.012f, silenceSeconds=.85f;
    public int sampleRate=16000;
    public bool waitForAnalysis;
    int localRevision;
    public void ClearRound() { localRevision++; lines.Clear(); subtitle="新一轮：请说出对方台词。"; speaking=false; utterance.Clear(); preroll.Clear(); }
    AudioClip clip;
    string microphone;
    readonly List<float> utterance=new List<float>();
    readonly Queue<float> preroll=new Queue<float>();
    int cursor, voicedSamples, submissions;
    float quietTime, level, nextRead;
    bool speaking, busy, active;
    string status="Starting microphone", subtitle="说一句话，停顿后字幕会出现在这里。";
    readonly List<string> lines=new List<string>();
    [Serializable] class Reply { public string text, error, source; public int elapsed_ms; }
    [Serializable] class Evidence { public string source="headset_microphone", microphone, status; public int submissions; public float rms; public bool focused; }
    IEnumerator Start() {
#if UNITY_ANDROID && !UNITY_EDITOR
        if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) {
            status="请允许麦克风权限";
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            float deadline=Time.realtimeSinceStartup+60;
            while(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)
                && Time.realtimeSinceStartup<deadline) yield return null;
            if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) {
                status="麦克风未授权，请在应用权限中开启"; yield break;
            }
        }
#endif
        yield return new WaitForSecondsRealtime(1);
        StartMicrophone();
    }
    void StartMicrophone() {
        if(Microphone.devices.Length==0) { status="没有可用麦克风"; return; }
        microphone=Microphone.devices[0];
        clip=Microphone.Start(microphone,true,4,sampleRate);
        if(clip==null) { status="麦克风启动失败"; return; }
        sampleRate=clip.frequency; cursor=0; active=true; status="正在聆听 · 说话后停顿";
        Debug.Log("PICO_DIALOGUE_MIC: "+microphone+" rate="+sampleRate+" channels="+clip.channels);
    }
    void Update() {
        var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if(Camera.main!=null) {
            if(head.TryGetFeatureValue(CommonUsages.devicePosition,out var pos)) Camera.main.transform.localPosition=pos;
            if(head.TryGetFeatureValue(CommonUsages.deviceRotation,out var rot)) Camera.main.transform.localRotation=rot;
        }
        if(statusText!=null) statusText.text=(waitForAnalysis?"分析中，稍等再说":status)+"  ·  A 清空 / B 暂停";
        if(subtitleText!=null) subtitleText.text=Wrap(subtitle,24);
        bool down=false;
        var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        right.TryGetFeatureValue(CommonUsages.secondaryButton,out down);
        if(down && !buttonHeld) Toggle(); buttonHeld=down;
        if(waitForAnalysis && clip!=null) { cursor=Math.Max(0,Microphone.GetPosition(microphone)); preroll.Clear(); return; }
        if(!active || busy || clip==null || !Application.isFocused || Time.realtimeSinceStartup<nextRead) return;
        nextRead=Time.realtimeSinceStartup+.05f;
        int end=Microphone.GetPosition(microphone);
        if(end<0) { status="麦克风位置读取失败"; return; }
        int count=(end-cursor+clip.samples)%clip.samples;
        if(count==0) return;
        // GetData wraps around the looping clip.
        var interleaved=new float[count*clip.channels];
        if(!clip.GetData(interleaved,cursor)) { status="麦克风数据读取失败"; return; }
        cursor=end;
        var mono=new float[count]; double energy=0;
        for(int i=0;i<count;i++) {
            float value=0;
            for(int c=0;c<clip.channels;c++) value+=interleaved[i*clip.channels+c];
            value/=clip.channels; mono[i]=value; energy+=value*value;
        }
        level=(float)Math.Sqrt(energy/count);
        if(!speaking) {
            foreach(float value in mono) { preroll.Enqueue(value); if(preroll.Count>sampleRate/4) preroll.Dequeue(); }
            if(level<voiceThreshold) return;
            speaking=true; utterance.Clear(); utterance.AddRange(preroll); preroll.Clear();
            voicedSamples=count; quietTime=0; status="正在录音 · 停顿即可提交";
        } else {
            utterance.AddRange(mono);
            if(level>=voiceThreshold) { quietTime=0; voicedSamples+=count; }
            else quietTime+=(float)count/sampleRate;
        }
        if(utterance.Count>=sampleRate*20 || quietTime>=silenceSeconds) {
            speaking=false;
            if(voicedSamples>=sampleRate/4) StartCoroutine(Transcribe(Wav(utterance,sampleRate)));
            else status="声音过短，请再说一句";
            utterance.Clear(); voicedSamples=0; quietTime=0;
        }
    }
    bool buttonHeld;
    void Toggle() {
        if(active) { StopMicrophone(); status="已暂停 · B键恢复"; }
        else StartMicrophone();
    }
    IEnumerator Transcribe(byte[] wav) {
        busy=true; submissions++; status="识别中…"; int sentRevision=localRevision;
        var form=new WWWForm(); form.AddBinaryData("file",wav,"headset.wav","audio/wav");
        using(var request=UnityWebRequest.Post(endpoint,form)) {
            request.timeout=50;
            yield return request.SendWebRequest();
            if(sentRevision!=localRevision) { busy=false; yield break; }
            Reply reply=null;
            try { reply=JsonUtility.FromJson<Reply>(request.downloadHandler.text); } catch(Exception) {}
            if(request.result!=UnityWebRequest.Result.Success || reply==null || !string.IsNullOrEmpty(reply.error)) {
                status="识别失败："+(reply!=null && !string.IsNullOrEmpty(reply.error)?reply.error:request.error);
                Debug.LogWarning("PICO_SPEECH_ERROR: "+status);
            } else if(string.IsNullOrWhiteSpace(reply.text)) status="未识别到文字，请再说一次";
            else {
                waitForAnalysis=true; lines.Add(reply.text.Trim()); while(lines.Count>1) lines.RemoveAt(0);
                subtitle=string.Join("\n",lines); status="已识别 · "+reply.elapsed_ms+" ms";
                Debug.Log("PICO_SPEECH_RESULT: source="+reply.source+" chars="+reply.text.Length+" elapsed_ms="+reply.elapsed_ms);
            }
        }
        busy=false; preroll.Clear();
        if(clip!=null) cursor=Math.Max(0,Microphone.GetPosition(microphone));
        var evidence=new Evidence { microphone=microphone,status=status,submissions=submissions,rms=level,focused=Application.isFocused };
        File.WriteAllText(Path.Combine(Application.persistentDataPath,"fusion-mic-status.json"),JsonUtility.ToJson(evidence));
    }
    static string Wrap(string text,int width) {
        var output=new StringBuilder(); int col=0, rows=0;
        foreach(char c in text) {
            if(c=='\n') { output.Append(c); col=0; rows++; }
            else { if(col>=width) { output.Append('\n'); col=0; rows++; } output.Append(c); col++; }
            if(rows>=6) { output.Append("…"); break; }
        }
        return output.ToString();
    }
    public static byte[] Wav(List<float> samples,int rate) {
        using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream)) {
            int bytes=samples.Count*2;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+bytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate*2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            foreach(float f in samples) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(f,-1,1)*32767));
            return stream.ToArray();
        }
    }
    void StopMicrophone() {
        active=false; Microphone.End(microphone);
        if(clip!=null) Destroy(clip); clip=null;
        speaking=false; utterance.Clear(); preroll.Clear(); cursor=0;
    }
    void OnApplicationFocus(bool focused) {
        if(!focused && active) { StopMicrophone(); resume=true; status="应用失焦，麦克风暂停"; }
        else if(focused && resume) { resume=false; StartMicrophone(); }
    }
    bool resume;
    void OnDestroy() { StopMicrophone(); }
}
