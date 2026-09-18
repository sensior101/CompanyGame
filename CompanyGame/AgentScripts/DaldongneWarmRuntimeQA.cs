using System;
using UnityEngine;
using UnityEditor;
using CompanyGame.Daldongne;
public static class DaldongneWarmRuntimeQA
{
    public static object Verify()
    {
        if(!EditorApplication.isPlaying)throw new InvalidOperationException("Runtime check requires Play mode.");
        var w=UnityEngine.Object.FindAnyObjectByType<DaldongneVillageWalker>();
        if(!w)throw new InvalidOperationException("Walker missing.");
        var cc=w.GetComponent<CharacterController>();
        w.SetWalking(true);
        bool walkCamera=w.walking && !w.overview.enabled && Mathf.Approximately(w.viewCamera.orthographicSize,8);
        w.ResetToSpawn();
        bool reset=Vector3.Distance(w.transform.position,w.spawn)<.001f;
        w.SetWalking(false);
        bool overview=!w.walking && w.overview.enabled && Mathf.Approximately(w.viewCamera.orthographicSize,42);
        bool dimensions=Mathf.Abs(cc.radius-.35f)<.001f && Mathf.Abs(cc.height-1.8f)<.001f && Mathf.Abs(cc.stepOffset-.23f)<.001f;
        if(!walkCamera || !reset || !overview || !dimensions)throw new InvalidOperationException("Walking preview state check failed.");
        return new {passed=true,walkCamera,reset,overview,radius=cc.radius,height=cc.height,stepOffset=cc.stepOffset,slopeLimit=cc.slopeLimit};
    }
}
