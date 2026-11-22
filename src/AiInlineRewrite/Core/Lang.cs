namespace AiInlineRewrite.Core;

/// <summary>
/// A cheap, conservative guess of "is the user writing Portuguese?". It only decides how the bubble shows the
/// versions: a translation is shown as plain text, an English correction as a diff against what was typed.
/// Biased towards English: it says Portuguese only for a Portuguese-only accented letter or two distinct
/// Portuguese stopwords, so a real English correction is never hidden behind a translation.
/// </summary>
public static class Lang
{
    private static readonly char[] Separators =
        [' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '"', '\'', '(', ')'];

    // Letters that appear in Portuguese but essentially never in English words.
    private const string PtAccents = "ãõçáéíóúâêôàü";

    private static readonly HashSet<string> PtWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "eu", "você", "voce", "vc", "não", "nao", "sim", "acho", "que", "porque",
        "então", "entao", "isso", "aqui", "ali", "muito", "muita", "mais", "mas",
        "com", "uma", "para", "pra", "está", "esta", "estou", "são", "sao", "né",
        "ne", "obrigado", "obrigada", "bom", "dia", "tudo", "bem", "fazer", "tenho",
        "quero", "gente", "agora", "hoje", "ontem", "amanhã", "amanha", "coisa",
        "meu", "minha", "seu", "sua", "dele", "dela", "nós", "vamos", "fica",
        "ficar", "tem", "também", "tambem", "quando", "onde", "quem", "precisa",
        "preciso", "sobre", "prazo", "reunião", "reuniao", "depois", "manda", "mande",
        "arquivo", "puder", "pode", "posso", "vou", "vai", "tempo", "ajuda", "cliente",
        "entregar", "sei", "nada", "ainda", "já",
    };

    public static bool LooksPortuguese(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;

        foreach (char c in s)
            if (PtAccents.Contains(char.ToLowerInvariant(c))) return true;

        int hits = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in s.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            if (PtWords.Contains(w) && seen.Add(w) && ++hits >= 2) return true;

        return false;
    }
}
