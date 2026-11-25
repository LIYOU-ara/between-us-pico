using UnityEngine;

// Art-direction presets for the desktop composite, not an emotion classifier.
[ExecuteAlways]
public sealed class BetweenUsCompositePalette : MonoBehaviour
{
    public enum Style { SilverNeutral, CalmBlue, WarmChampagne, ReflectiveLilac, TenseRose }
    public Style style = Style.SilverNeutral;
    public Material[] panels, rings;
    public Material person, headset;
    int lastApplied = -1;
    // Saved material tuning is authoritative after editor/script reload.
    // Explicit style changes and menu presets still call Apply.
    void OnEnable(){lastApplied=(int)style;}
    void OnValidate(){lastApplied=-1;}
    void Update(){if(lastApplied!=(int)style)Apply(style);}
    public void Apply(Style value)
    {
        style=value;lastApplied=(int)value;
        Color frost=new Color(.49f,.515f,.55f,1), a=new Color(.66f,.70f,.77f,1), b=new Color(.82f,.84f,.89f,1), accent=new Color(.80f,.86f,.98f,1);
        float tint=.11f;
        switch(value)
        {
            case Style.CalmBlue: frost=new Color(.47f,.52f,.58f,1);a=new Color(.32f,.54f,.76f,1);b=new Color(.62f,.78f,.91f,1);accent=new Color(.55f,.76f,1,1);tint=.25f;break;
            case Style.WarmChampagne: frost=new Color(.54f,.52f,.50f,1);a=new Color(.75f,.58f,.41f,1);b=new Color(.92f,.80f,.66f,1);accent=new Color(1,.81f,.62f,1);tint=.22f;break;
            case Style.ReflectiveLilac: frost=new Color(.51f,.50f,.57f,1);a=new Color(.51f,.43f,.73f,1);b=new Color(.77f,.69f,.90f,1);accent=new Color(.79f,.67f,1,1);tint=.25f;break;
            case Style.TenseRose: frost=new Color(.55f,.49f,.52f,1);a=new Color(.72f,.36f,.46f,1);b=new Color(.88f,.63f,.68f,1);accent=new Color(1,.62f,.70f,1);tint=.22f;break;
        }
        if(panels!=null)foreach(var m in panels)
        {
            if(m==null)continue;m.SetColor("_FrostColor",frost);m.SetColor("_TintA",a);m.SetColor("_TintB",b);m.SetFloat("_TintStrength",tint);m.SetFloat("_Opacity",.60f);
            m.SetColor("_RimColor",new Color(.94f,.96f,1,1));m.SetFloat("_RimStrength",.77f);m.SetFloat("_FlowIntensity",1.05f);m.SetFloat("_FrostGrain",.0025f);
        }
        if(rings!=null)foreach(var m in rings)if(m!=null)m.SetColor("_Color",Color.Lerp(new Color(.86f,.91f,1,1),accent,.55f));
        if(person!=null){person.SetColor("_Color",new Color(.76f,.79f,.83f,1));person.SetColor("_AccentColor",accent);person.SetFloat("_AccentStrength",.025f);}
        if(headset!=null)headset.SetColor("_AccentColor",accent);
    }
}
