using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Results are snapshot descriptions from a PC bridge, never emotion diagnoses.
public class PicoVisionOverlay : MonoBehaviour {
    [Serializable] public class Result {
        public string status, message, uncertainty;
        public string[] observations;
        public bool readable, auto;
        public long capture_unix_ms, result_unix_ms;
        public int elapsed_ms, cloud_calls;
    }
    public TextMesh display;
    const string Endpoint="http://127.0.0.1:8793/result";
    Result current;
    string error="等待电脑视觉服务…";
    float received=-100;
    IEnumerator Start() {
        while(true) {
            using(var request=UnityWebRequest.Get(Endpoint)) {
                request.timeout=3;
                yield return request.SendWebRequest();
                if(request.result==UnityWebRequest.Result.Success) {
                    try {
                        current=JsonUtility.FromJson<Result>(request.downloadHandler.text);
                        received=Time.realtimeSinceStartup; error=null;
                    } catch { current=null; error="结果格式错误"; }
                } else { current=null; error="电脑视觉服务未连接\n请运行 Start-Pico-Vision.cmd"; }
            }
            yield return new WaitForSecondsRealtime(.5f);
        }
    }
    void Update() {
        if(display==null)return;
        string text="PICO · 面部动作读取\n";
        if(current==null || Time.realtimeSinceStartup-received>5) { display.text=text+(error??"服务连接已中断");return; }
        text+=Wrap(current.message,26)+"\n";
        long age=current.capture_unix_ms>0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-current.capture_unix_ms : -1;
        if(current.status=="ready" && age>=0 && age<=15000) {
            text+="这一帧可见：\n";
            if(current.readable && current.observations!=null)
                foreach(string observation in current.observations)text+="· "+Wrap(observation,25)+"\n";
            else text+="无法可靠读取\n";
            text+=Wrap(current.uncertainty,26)+"\n";
        } else if(current.status=="ready") text+="旧画面结果已隐藏\n请重新读取\n";
        else if(current.status=="paused") text+="默认暂停；电脑页面开启读取\n";
        if(age>=0)text+="画面年龄 "+(age/1000f).ToString("F1")+" 秒 / 处理 "+current.elapsed_ms+" ms\n";
        text+="可见动作 ≠ 情绪或真实意图";
        display.text=text;
    }
    static string Wrap(string text,int width) {
        if(string.IsNullOrEmpty(text))return "";
        var result=new StringBuilder();int count=0;
        foreach(char c in text) { result.Append(c); if(c=='\n')count=0;else if(++count>=width){result.Append('\n');count=0;} }
        return result.ToString();
    }
}
