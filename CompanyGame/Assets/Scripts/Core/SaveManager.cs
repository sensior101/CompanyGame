using UnityEngine;

public class SaveManager : MonoBehaviour
{
    /// <summary>Writes a complete JSON snapshot without overwriting the live file in place.
    /// Content owners retain their schema, path, cache and error handling.</summary>
    public static void WriteJsonFile(string path, string json)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath));
        string temporary = fullPath + ".tmp";
        System.IO.File.WriteAllText(temporary, json);
        if (System.IO.File.Exists(fullPath)) System.IO.File.Replace(temporary, fullPath, null);
        else System.IO.File.Move(temporary, fullPath);
    }
}
