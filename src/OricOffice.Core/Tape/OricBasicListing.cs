using System.Text;

namespace OricOffice.Core.Tape;

/// <summary>
/// Turns an Oric BASIC memory image (as saved to tape from $0501) back into a
/// readable listing. Program lines are a linked list:
/// [next-line pointer, LE16][line number, LE16][tokenised text][0x00];
/// the program ends at a zero next-pointer. Keywords are single bytes ≥ $80,
/// while numbers, strings, variables and punctuation stay plain ASCII.
///
/// The token table comes from the Oric Atmos manual appendix, cross-checked
/// against the real Tansoft tapes (GRAB, HIMEM, PRINT@, CHR$, the "+" operator
/// at $CC and "=" at $D4 all verified in situ); unknown tokens pass through.
/// </summary>
public static class OricBasicListing
{
    private static readonly string?[] Tokens = BuildTokens();

    /// <summary>Detokenizes a BASIC program image into "10 PRINT…" lines.
    /// <paramref name="loadAddress"/> is where the image sits in memory, because
    /// each line's next-pointer is an absolute address.</summary>
    public static string Detokenize(byte[] image, int loadAddress = 0x0501)
    {
        var builder = new StringBuilder();
        if (image is null || image.Length < 4) return string.Empty;

        int position = 0;
        while (position <= image.Length - 4)
        {
            int next = image[position] | (image[position + 1] << 8);
            int number = image[position + 2] | (image[position + 3] << 8);
            int nextOffset = next - loadAddress;
            if (next == 0 || nextOffset <= position || nextOffset > image.Length) break; // end

            var text = new StringBuilder();
            for (int i = position + 4; i < nextOffset && i < image.Length; i++)
            {
                byte b = image[i];
                if (b == 0x00) break; // line terminator
                if (b >= 0x80 && Tokens[b - 0x80] is { } keyword)
                {
                    if (text.Length > 0 && !char.IsPunctuation(text[^1]) && keyword[0] is not ('$' or '%'))
                        text.Append(' ');
                    text.Append(keyword);
                    if (keyword is "REM" or "DATA")
                        text.Append(' '); // the rest of the line is literal
                }
                else
                {
                    text.Append((char)b);
                }
            }

            builder.Append(number).Append(' ').Append(text.ToString().TrimEnd()).Append('\n');
            position = nextOffset;
        }

        return builder.ToString();
    }

    /// <summary>Extracts the readable text of a binary block (for listings).</summary>
    public static string PrintableRuns(byte[] payload, int maxRuns = 400)
    {
        var builder = new StringBuilder();
        var run = new StringBuilder();
        int shown = 0;

        for (int i = 0; i < payload.Length && shown < maxRuns; i++)
        {
            char c = (char)payload[i];
            if (c is >= ' ' and <= '~')
            {
                run.Append(c);
                continue;
            }

            if (run.Length >= 6)
            {
                builder.Append('+').Append(i - run.Length).Append(": \"").Append(run).Append("\"\n");
                shown++;
            }
            run.Clear();
        }

        if (run.Length >= 6 && shown < maxRuns)
            builder.Append("end: \"").Append(run).Append("\"\n");

        return builder.ToString();
    }

    private static string?[] BuildTokens()
    {
        var tokens = new string?[0x80];

        void Set(int code, string keyword) => tokens[code - 0x80] = keyword;

        // Oric Atmos BASIC keyword appendix (defence-force.org), plus values
        // confirmed against the Tansoft tapes.
        Set(0x80, "END"); Set(0x81, "EDIT"); Set(0x82, "STORE"); Set(0x83, "RECALL");
        Set(0x84, "TRON"); Set(0x85, "TROFF"); Set(0x86, "POP"); Set(0x87, "PRINT@");
        Set(0x88, "PULL"); Set(0x89, "LORES"); Set(0x8A, "DOKE"); Set(0x8B, "REPEAT");
        Set(0x8C, "UNTIL"); Set(0x8D, "FOR"); Set(0x8E, "LLIST"); Set(0x8F, "LPRINT");
        Set(0x90, "NEXT"); Set(0x91, "DATA"); Set(0x92, "INPUT#"); Set(0x93, "DIM");
        Set(0x94, "CLS"); Set(0x95, "READ"); Set(0x96, "LET"); Set(0x97, "GOTO");
        Set(0x98, "RUN"); Set(0x99, "IF"); Set(0x9A, "RESTORE"); Set(0x9B, "GOSUB");
        Set(0x9C, "RETURN"); Set(0x9D, "REM"); Set(0x9E, "HIMEM"); Set(0x9F, "GRAB");

        Set(0xA0, "RELEASE"); Set(0xA1, "TEXT"); Set(0xA2, "HIRES"); Set(0xA3, "SHOOT");
        Set(0xA4, "EXPLODE"); Set(0xA5, "ZAP"); Set(0xA6, "PING"); Set(0xA7, "SOUND");
        Set(0xA8, "MUSIC"); Set(0xA9, "PLAY"); Set(0xAA, "CURSET"); Set(0xAB, "CURMOV");
        Set(0xAC, "DRAW"); Set(0xAD, "CIRCLE"); Set(0xAE, "PATTERN"); Set(0xAF, "FILL");

        Set(0xB1, "PAPER"); Set(0xB2, "INK"); Set(0xB3, "STOP"); Set(0xB4, "ON");
        Set(0xB5, "WAIT"); Set(0xB6, "CLOAD"); Set(0xB7, "CSAVE"); Set(0xB8, "DEF");
        Set(0xB9, "POKE"); Set(0xBA, "PRINT"); Set(0xBB, "CONT"); Set(0xBC, "LIST");
        Set(0xBD, "CLEAR"); Set(0xBE, "GET"); Set(0xBF, "CALL");

        Set(0xC1, "NEW"); Set(0xC2, "TAB("); Set(0xC3, "TO"); Set(0xC4, "FN");
        Set(0xC5, "SPC("); Set(0xC7, "AUTO"); Set(0xC8, "ELSE"); Set(0xC9, "THEN");
        Set(0xCA, "NOT"); Set(0xCB, "STEP");

        Set(0xD6, "SGN"); Set(0xD7, "INT"); Set(0xD8, "ABS"); Set(0xD9, "USR");
        Set(0xDA, "FRE"); Set(0xDB, "POS"); Set(0xDC, "HEX$"); Set(0xDE, "SQR");
        Set(0xDF, "RND");

        Set(0xE0, "LN"); Set(0xE1, "EXP"); Set(0xE2, "COS"); Set(0xE3, "SIN");
        Set(0xE4, "TAN"); Set(0xE5, "ATN"); Set(0xE6, "PEEK"); Set(0xE7, "DEEK");
        Set(0xE8, "LOG"); Set(0xE9, "LEN"); Set(0xEB, "VAL"); Set(0xEC, "ASC");
        Set(0xED, "CHR$"); Set(0xEE, "PI"); Set(0xEF, "TRUE");

        Set(0xF0, "FALSE"); Set(0xF1, "KEY$"); Set(0xF2, "SCRN("); Set(0xF3, "POINT");
        Set(0xF4, "LEFT$"); Set(0xF5, "RIGHT$"); Set(0xF7, "GO TO");

        // Verified in the Tansoft programs themselves.
        Set(0xCC, "+");  // string/numeric concatenation (CHR$(27)<CC>" ")
        Set(0xD4, "=");  // assignment (QU$<D4>CHR$(34))

        return tokens;
    }
}
