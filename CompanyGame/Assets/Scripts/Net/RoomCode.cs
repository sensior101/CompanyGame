using System.Text;
using UnityEngine;

/// <summary>
/// The 6-character room code (the server's room id). Players read it out or paste it, so typed text is forgiven:
/// any case, spaces, hyphens. The server's alphabet leaves out 0 O 1 I, so a code never contains them.
/// Everything that only displays or forwards a room id must accept any string (an older server hands out ids like "r1").
/// </summary>
public static class RoomCode
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    /// <summary>Typed or pasted text -> upper-case letters and digits only (spaces, hyphens and everything else dropped).</summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
            if (c < 128 && char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    /// <summary>True for a string the current server could have handed out as a room code.</summary>
    public static bool IsCode(string rid)
    {
        if (rid == null || rid.Length != Length) return false;
        foreach (char c in rid) if (Alphabet.IndexOf(c) < 0) return false;
        return true;
    }

    public static void Copy(string rid) { GUIUtility.systemCopyBuffer = rid ?? ""; }
}
