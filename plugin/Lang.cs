using System.Collections.Generic;
using System.Globalization;

namespace CatAPI
{
    /// <summary>
    /// 英语是每句都有的底。其他语言挂在这句后面，游戏语言对上就用，对不上就用英语。
    /// Lang.En("Set slot count").ZhCN("设置槽位数目").JaJP("スロット数")
    /// </summary>
    public static class Lang
    {
        public const int ZhCN = 2052;
        public const int EnUS = 1033;
        public const int FrFR = 1036;
        public const int DeDE = 1031;
        public const int EsES = 3082;
        public const int JaJP = 1041;
        public const int KoKR = 1042;

        public static int Lcid
        {
            get
            {
                try
                {
                    if (Localization.Loaded)
                    {
                        return Localization.CurrentLanguageLCID;
                    }
                }
                catch
                {
                    // 游戏类型不可用时退回系统语言。
                }
                return CultureInfo.CurrentUICulture.LCID;
            }
        }

        public static bool IsChinese => (Lcid & 0x3FF) == 0x04;

        public static Line En(string en)
        {
            return new Line(en);
        }

        public sealed class Line
        {
            readonly string en;
            readonly Dictionary<int, string> text = new Dictionary<int, string>();

            internal Line(string en)
            {
                this.en = en ?? "";
            }

            public Line ZhCN(string value) => Set(Lang.ZhCN, value);
            public Line FrFR(string value) => Set(Lang.FrFR, value);
            public Line DeDE(string value) => Set(Lang.DeDE, value);
            public Line EsES(string value) => Set(Lang.EsES, value);
            public Line JaJP(string value) => Set(Lang.JaJP, value);
            public Line KoKR(string value) => Set(Lang.KoKR, value);

            public Line Set(int lcid, string value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    text[lcid] = value;
                }
                return this;
            }

            public static implicit operator string(Line line)
            {
                return line == null ? "" : line.ToString();
            }

            public override string ToString()
            {
                int lcid = Lcid;
                if (text.TryGetValue(lcid, out string exact))
                {
                    return exact;
                }
                int primary = lcid & 0x3FF;
                foreach (KeyValuePair<int, string> pair in text)
                {
                    if ((pair.Key & 0x3FF) == primary)
                    {
                        return pair.Value;
                    }
                }
                return en;
            }
        }
    }
}
