using System;
using System.IO;
using UnityEngine;
using CompanyGame.Daldongne;
public static class DaldongneWarmScreenshots
{
    public static object Capture()
    {
        var control=UnityEngine.Object.FindAnyObjectByType<DaldongneMapCamera>();
        if(!control)throw new InvalidOperationException("Map camera missing.");
        var camera=control.GetComponent<Camera>();
        Vector3 oldFocus=control.focus;float oldZoom=control.zoom,oldPitch=control.pitch,oldYaw=control.yaw;
        string dir=Path.GetFullPath("Assets/Screenshots");Directory.CreateDirectory(dir);
        try
        {
            Shot(control,camera,dir,"DaldongneWarm_Overview",new Vector3(0,6,2),42,false);
            Shot(control,camera,dir,"DaldongneWarm_TopDown",new Vector3(0,6,0),37,true);
            Shot(control,camera,dir,"DaldongneWarm_Shops",new Vector3(21,6,0),11,false);
            Shot(control,camera,dir,"DaldongneWarm_CityHall",new Vector3(7,10,7),12,false);
            Shot(control,camera,dir,"DaldongneWarm_Station",new Vector3(-17.5f,2,-22),10,false);
        }
        finally
        {
            control.focus=oldFocus;control.zoom=oldZoom;control.SetTopDown(oldPitch>80);control.pitch=oldPitch;control.yaw=oldYaw;
        }
        return new {directory=dir,images=5};
    }
    static void Shot(DaldongneMapCamera control,Camera camera,string dir,string name,Vector3 focus,float zoom,bool top)
    {
        control.focus=focus;control.zoom=zoom;control.SetTopDown(top);
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
        var target=RenderTexture.GetTemporary(1100,1100,24,RenderTextureFormat.ARGB32);
        var texture=new Texture2D(1100,1100,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,1100,1100),0,0);texture.Apply();
            File.WriteAllBytes(Path.Combine(dir,name+".png"),texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=oldTarget;RenderTexture.active=oldActive;
            RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
