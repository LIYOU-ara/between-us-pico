#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

// Patch the generated Unity library manifest after PICO's processor has finished.
public sealed class PicoCameraPermission : IPostGenerateGradleAndroidProject {
    public int callbackOrder => 20000;
    public void OnPostGenerateGradleAndroidProject(string path) {
        string file=Path.Combine(path,"src/main/AndroidManifest.xml");
        var xml=new XmlDocument(); xml.Load(file);
        const string android="http://schemas.android.com/apk/res/android";
        foreach(string name in new[]{"android.permission.CAMERA","android.permission.INTERNET","android.permission.RECORD_AUDIO"}) {
            bool found=false;
            foreach(XmlNode item in xml.DocumentElement.SelectNodes("uses-permission"))
                if(item.Attributes?["name",android]?.Value==name) found=true;
            if(found) continue;
            var permission=xml.CreateElement("uses-permission");
            var attribute=xml.CreateAttribute("android","name",android); attribute.Value=name;
            permission.Attributes.Append(attribute); xml.DocumentElement.AppendChild(permission);
        }
        xml.Save(file);
    }
}
#endif
