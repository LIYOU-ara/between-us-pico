using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

public class PicoLocalFace : MonoBehaviour {
 [Serializable] public class FacePacket {public bool tracked;public long frame_unix_ms;public float[] ray,head_position,head_rotation,camera_position,camera_rotation;}
 public FacePacket face=new FacePacket();
 public byte[] crop;public long cropTime;
 public string diagnostic="等待相机",diagnosticCode="camera_wait",processingStage="frame";
 public float frameAgeMs=-1,detectionMs;public int detectedFaces,bitmapWidth,bitmapHeight;
 Texture2D texture;string lastLoggedCode="";float nextDiagnosticLog;

 IEnumerator Start(){
  while(true){
   using(var req=UnityWebRequest.Get("http://127.0.0.1:8890/frame")){
    req.timeout=2;yield return req.SendWebRequest();
    if(req.result==UnityWebRequest.Result.Success){
     try{Process(req.downloadHandler.data,req.GetResponseHeader("X-Camera-Meta"),req.GetResponseHeader("X-Frame-Age-Ms"));}
     catch(Exception e){Clear();SetDiagnostic("detector_error","人脸检测错误："+processingStage+" / "+e.GetType().Name);Debug.LogWarning("PICO_LOCAL_FACE_ERROR stage="+processingStage+" type="+e.GetType().Name+" detail="+SafeException(e.Message));}
    }else{Clear();SetDiagnostic("camera_wait","等待头显相机帧");}
   }
   yield return new WaitForSecondsRealtime(.2f);
  }
 }
 void Clear(){face.tracked=false;crop=null;detectedFaces=0;}
 void SetDiagnostic(string code,string text){
  diagnosticCode=code;diagnostic=text;
  if(lastLoggedCode!=code&&Time.realtimeSinceStartup>=nextDiagnosticLog){
   lastLoggedCode=code;nextDiagnosticLog=Time.realtimeSinceStartup+2;
   Debug.Log("PICO_LOCAL_FACE status="+code+" stage="+processingStage+" age_ms="+frameAgeMs.ToString("F0",CultureInfo.InvariantCulture)+" faces="+detectedFaces+" bitmap="+bitmapWidth+"x"+bitmapHeight+" detect_ms="+detectionMs.ToString("F0",CultureInfo.InvariantCulture));
  }
 }
 static string SafeException(string text){if(string.IsNullOrEmpty(text))return "none";text=text.Replace('\r',' ').Replace('\n',' ');return text.Length<=180?text:text.Substring(0,180);}
 static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
 static bool VectorValid(float[] a,int length){if(a==null||a.Length!=length)return false;foreach(float v in a)if(!Finite(v))return false;return true;}

 void Process(byte[] jpeg,string encoded,string ageValue){
  Clear();processingStage="frame";detectionMs=0;
  if(!float.TryParse(ageValue,NumberStyles.Float,CultureInfo.InvariantCulture,out frameAgeMs)||!Finite(frameAgeMs)||frameAgeMs<0||frameAgeMs>500){SetDiagnostic("frame_stale","相机帧已过期");return;}
  if(jpeg==null||jpeg.Length==0){SetDiagnostic("frame_empty","相机帧为空");return;}
  if(string.IsNullOrEmpty(encoded)){SetDiagnostic("metadata_missing","相机信息缺失");return;}
  processingStage="metadata";
  var meta=JsonUtility.FromJson<PicoSdkCameraCapture.CameraStatus>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
  if(meta==null||meta.frame_unix_ms<=0){SetDiagnostic("metadata_invalid","相机时间戳缺失");return;}
#if UNITY_ANDROID && !UNITY_EDITOR
  float started=Time.realtimeSinceStartup;
  processingStage="bitmap_decode";
  var javaJpeg=new sbyte[jpeg.Length];Buffer.BlockCopy(jpeg,0,javaJpeg,0,jpeg.Length);
  using(var factory=new AndroidJavaClass("android.graphics.BitmapFactory"))
  using(var bitmap=factory.CallStatic<AndroidJavaObject>("decodeByteArray",javaJpeg,0,javaJpeg.Length)){
   if(bitmap==null){SetDiagnostic("bitmap_invalid","相机图像无法解码");return;}
   bitmapWidth=bitmap.Call<int>("getWidth");bitmapHeight=bitmap.Call<int>("getHeight");
   if(bitmapWidth<2||bitmapHeight<2){bitmap.Call("recycle");SetDiagnostic("bitmap_invalid","相机图像尺寸异常");return;}
   // Android FaceDetector requires RGB_565 and an even bitmap width. The decoded
   // JPEG dimensions are authoritative; calibration may use a different size.
   int detectionWidth=bitmapWidth&~1;
   using(var bitmapClass=new AndroidJavaClass("android.graphics.Bitmap"))
   using(var even=bitmapClass.CallStatic<AndroidJavaObject>("createBitmap",bitmap,0,0,detectionWidth,bitmapHeight))
   using(var config=new AndroidJavaClass("android.graphics.Bitmap$Config"))
   using(var rgb=even.Call<AndroidJavaObject>("copy",config.GetStatic<AndroidJavaObject>("RGB_565"),false))
   using(var detector=new AndroidJavaObject("android.media.FaceDetector",detectionWidth,bitmapHeight,3))
   using(var classes=new AndroidJavaClass("java.lang.Class"))
   using(var reflect=new AndroidJavaClass("java.lang.reflect.Array"))
   using(var type=classes.CallStatic<AndroidJavaObject>("forName","android.media.FaceDetector$Face"))
   using(var faces=reflect.CallStatic<AndroidJavaObject>("newInstance",type,3)){
    try{
     processingStage="find_faces";
     var findId=AndroidJNI.GetMethodID(detector.GetRawClass(),"findFaces","(Landroid/graphics/Bitmap;[Landroid/media/FaceDetector$Face;)I");
     detectedFaces=detector.Call<int>(findId,rgb,faces);
     detectionMs=(Time.realtimeSinceStartup-started)*1000;
     if(detectedFaces!=1){SetDiagnostic(detectedFaces==0?"face_none":"face_multiple",detectedFaces==0?"请对准清晰正面脸":"多张脸，暂停定位");return;}
     processingStage="face_geometry";
     var getId=AndroidJNI.GetStaticMethodID(reflect.GetRawClass(),"get","(Ljava/lang/Object;I)Ljava/lang/Object;");
     using(var f=reflect.CallStatic<AndroidJavaObject>(getId,faces,0))
     using(var point=new AndroidJavaObject("android.graphics.PointF")){
      float confidence=f.Call<float>("confidence");
      if(confidence<.4f){SetDiagnostic("face_unclear","人脸清晰度不足");return;}
      f.Call("getMidPoint",point);
      float midX=point.Get<float>("x"),midY=point.Get<float>("y"),eyes=f.Call<float>("eyesDistance");
      if(!Finite(midX)||!Finite(midY)||!Finite(eyes)||eyes<=0){SetDiagnostic("face_geometry_invalid","人脸坐标异常");return;}
      int x=Mathf.Clamp(Mathf.RoundToInt(midX-eyes*1.7f),0,bitmapWidth-2),y=Mathf.Clamp(Mathf.RoundToInt(midY-eyes*1.45f),0,bitmapHeight-2);
      int width=Mathf.Clamp(Mathf.RoundToInt(eyes*3.4f),2,bitmapWidth-x),height=Mathf.Clamp(Mathf.RoundToInt(eyes*4f),2,bitmapHeight-y);
      if(width<60||height<60){SetDiagnostic("face_small","请靠近一些");return;}
      processingStage="face_crop";
      if(texture==null)texture=new Texture2D(2,2,TextureFormat.RGB24,false);
      if(!texture.LoadImage(jpeg)){SetDiagnostic("bitmap_invalid","相机图像无法解码");return;}
      var small=new Texture2D(width,height,TextureFormat.RGB24,false);
      try{small.SetPixels(texture.GetPixels(x,bitmapHeight-y-height,width,height));small.Apply();crop=small.EncodeToJPG(85);cropTime=meta.frame_unix_ms;}
      finally{Destroy(small);}
      // Face upload needs only a fresh crop. Spatial text anchoring additionally
      // needs calibrated intrinsics/extrinsics; one must not disable the other.
      processingStage="face_pose";
      bool poseValid=meta.calibrated&&meta.extrinsics_valid&&meta.fx>0&&meta.fy>0&&Finite(meta.fx)&&Finite(meta.fy)
       &&VectorValid(meta.head_position,3)&&VectorValid(meta.head_rotation,4)&&VectorValid(meta.camera_position,3)&&VectorValid(meta.camera_rotation,4);
      if(poseValid){
       float scaleX=meta.width/(float)bitmapWidth,scaleY=meta.height/(float)bitmapHeight;
       face=new FacePacket{tracked=true,frame_unix_ms=meta.frame_unix_ms,ray=new[]{(midX*scaleX-meta.cx)/meta.fx,-((y-eyes*.2f)*scaleY-meta.cy)/meta.fy,1f},head_position=meta.head_position,head_rotation=meta.head_rotation,camera_position=meta.camera_position,camera_rotation=meta.camera_rotation};
       SetDiagnostic("face_ready","单人脸已定位");
      }else SetDiagnostic("crop_ready_pose_missing","照片已取得，定位信息未就绪");
     }
    }finally{rgb.Call("recycle");if(!AndroidJNI.IsSameObject(even.GetRawObject(),bitmap.GetRawObject()))even.Call("recycle");bitmap.Call("recycle");}
   }
  }
#else
  SetDiagnostic("android_required","请在 Android 头显运行");
#endif
 }
 void OnDestroy(){if(texture!=null)Destroy(texture);}
}
