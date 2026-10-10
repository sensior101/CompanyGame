/// <summary>Shared Korean particles for dynamic names and titles, independent of UI or content.</summary>
public static class KoreanText
{
    /// <summary>Appends 이/가 according to the last pronounced Hangul character.</summary>
    public static string Subject(string text) => AppendParticle(text, "이", "가");

    /// <summary>Appends 을/를 according to the last pronounced Hangul character.</summary>
    public static string Object(string text) => AppendParticle(text, "을", "를");

    static string AppendParticle(string text, string consonant, string vowel)
    {
        string noun = (text ?? string.Empty).TrimEnd();
        if (noun.Length == 0) return string.Empty;

        bool? hasFinalConsonant = FinalConsonant(noun);
        if (hasFinalConsonant.HasValue)
            return noun + (hasFinalConsonant.Value ? consonant : vowel);

        // Foreign words and symbols do not determine pronunciation reliably.
        // Keep both forms instead of guessing from the final Latin letter.
        return noun + "(" + consonant + ")" + vowel;
    }

    static bool? FinalConsonant(string text)
    {
        for (int i = text.Length - 1; i >= 0; --i)
        {
            char last = text[i];
            // Closing quotes/brackets and punctuation remain in the displayed title,
            // but do not change the particle: e.g. 『책』이, 안녕하세요!가.
            if (char.IsWhiteSpace(last) || char.IsPunctuation(last)) continue;

            if (last >= '\uAC00' && last <= '\uD7A3')
                return (last - '\uAC00') % 28 != 0;

            // Canonically decomposed Hangul ends in a vowel or a trailing consonant.
            if (last >= '\u11A8' && last <= '\u11C2') return true;
            if (last >= '\u1161' && last <= '\u1175') return false;
            if (last >= '\u3131' && last <= '\u314E') return true;
            if (last >= '\u314F' && last <= '\u3163') return false;

            // Arabic numerals use Korean number readings (영/일/삼/육/칠/팔).
            // Numbers ending in zero also end in a consonant (십/백/천/만...).
            if (last >= '0' && last <= '9')
                return last == '0' || last == '1' || last == '3' ||
                       last == '6' || last == '7' || last == '8';

            return null;
        }
        return null;
    }
}
