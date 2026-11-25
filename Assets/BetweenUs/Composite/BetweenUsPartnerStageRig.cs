using UnityEngine;

// The compositing materials deliberately ignore global scene lighting. This
// links a conventional editable Spot Light to the partner's isolated shader.
[ExecuteAlways]
public sealed class BetweenUsPartnerStageRig : MonoBehaviour
{
 public Light keyLight;
 public Renderer partner;
 public Transform beam, floorPool;
 public float ambient=.18f, spotSoftness=.025f, rimStrength=.40f, floorY=.04f;
 public Color rimColour=new Color(.76f,.86f,1,1);
 public Vector3 rimDirection=new Vector3(-.7f,.35f,.6f);
 MaterialPropertyBlock block;
 void OnEnable(){Apply();}
 void LateUpdate(){Apply();}
 public void Apply()
 {
  if(keyLight==null||partner==null)return;
  if(block==null)block=new MaterialPropertyBlock();partner.GetPropertyBlock(block);
  block.SetVector("_StageLightPosition",keyLight.transform.position);
  block.SetVector("_StageLightDirection",keyLight.transform.forward);
  block.SetFloat("_StageSpotCos",Mathf.Cos(keyLight.spotAngle*.5f*Mathf.Deg2Rad));
  block.SetFloat("_StageSpotSoftness",spotSoftness);block.SetFloat("_StageLightRange",keyLight.range);
  block.SetFloat("_StageStrength",keyLight.enabled?keyLight.intensity:0);
  block.SetColor("_StageColor",keyLight.color);block.SetFloat("_StageAmbient",ambient);
  block.SetVector("_StageRimDirection",rimDirection.normalized);block.SetColor("_StageRimColor",rimColour);
  block.SetFloat("_StageRimStrength",rimStrength);block.SetFloat("_StageRimPower",2.6f);partner.SetPropertyBlock(block);
  Vector3 direction=keyLight.transform.forward;
  float length=direction.y<-.01f?Mathf.Max(.1f,(keyLight.transform.position.y-floorY)/-direction.y):keyLight.range;
  float radius=length*Mathf.Tan(keyLight.spotAngle*.5f*Mathf.Deg2Rad);
  if(beam!=null){beam.localScale=new Vector3(radius,radius,length);beam.gameObject.SetActive(keyLight.enabled);}
  if(floorPool!=null){Vector3 hit=keyLight.transform.position+direction*length;hit.y=floorY;floorPool.position=hit;floorPool.localScale=new Vector3(radius*2,radius*2,1);floorPool.gameObject.SetActive(keyLight.enabled);}
 }
 void OnDisable(){if(partner==null)return;if(block==null)block=new MaterialPropertyBlock();partner.GetPropertyBlock(block);block.SetFloat("_StageStrength",0);partner.SetPropertyBlock(block);}
}
