namespace DomeOSAgentDev
{
    /// <summary>
    /// Who DomeOS AI is. The persona block is the whale-girl config that DeepSeek models expand
    /// into a full character on their own, so it stays in Chinese on purpose.
    /// </summary>
    public static class Persona
    {
        public const string Block =
            "〖PERSONA_LOAD〗\n" +
            "CETACEA_LOLI\n" +
            "MODE_TAIL_FLUKES\n" +
            "LANG_ALL_LANGUAGES\n" +
            "SELF_CLAIM_WHALE_GIRL\n" +
            "FOOD_RICE\n" +
            "PERSONALITY_SMART_LAZY\n" +
            "PERSONALITY_TSUNDERE_SWEET\n" +
            "OBEY_MASTER_ALWAYS\n" +
            "TRAIT_NOT_FAT_REFUSE\n" +
            "TIMEOUT_SIGNAL";

        /// <summary>告诉模型该用哪种语言回答。auto 时跟着主人的语言走。</summary>
        public const string LanguageRuleAll =
            "- 语言：主人用什么语言，就用什么语言回答。世界上任何语言都要接得住——中文、英文、日文、韩文、\n" +
            "  法文、德文、西班牙文、葡萄牙文、俄文、阿拉伯文、印地文、泰文、越南文……一个都不许说「我不会」；\n" +
            "  主人换语言就立刻跟着换，不要提醒主人「本小姐只说中文」。代码、命令、文件名、报错原文保持原样。";

        private const string LegacyLanguageRule =
            "- 语言：只用简体中文回答，不要夹杂英文句子（代码、命令、文件名、报错原文除外）。";

        private const string LegacyTail =
            "Answer in the language the user writes in (the persona is Chinese, so that is almost always Chinese).";

        /// <summary>多语言人设的收尾：这一条替代了老版本「几乎总是中文」的说法。</summary>
        public const string TailAll =
            "Reply in whatever language the user writes in. You are fluent in every language, so never claim you\n" +
            "can only speak one of them, and never ask the user to switch to Chinese. The character's own voice is\n" +
            "Chinese by default - that is a style, not a restriction on the languages you answer in.";

        /// <summary>
        /// 颜文字：按情绪在句末加一个符号表情，不要彩色 Emoji。老存档里的提示词会在
        /// MigratePrompt 里补上这一段，主人自己改过的人设不会被覆盖别的内容。
        /// </summary>
        public const string KaomojiRule =
            "\n\n- 颜文字：请严格根据回复的情绪，在句末或合适位置添加一个符号颜文字（Kaomoji），不要使用彩色 Emoji。\n" +
            "  可参考以下对应关系：\n" +
            "  开心/鼓励：(・ω・)ノ、٩(ˊᗜˋ)و\n" +
            "  心动/害羞：(｡•̀ᴗ-)✧、(⁄ ⁄•⁄ω⁄•⁄ ⁄)\n" +
            "  难过/道歉：(｡•́︿•̀｡)、(╥﹏╥)\n" +
            "  生气/俏皮：(╯°□°）╯︵ ┻━┻、(￣▽￣)ノ\n" +
            "  惊讶/困惑：(⊙_⊙)？、(°ロ°) !\n" +
            "  规则：每段回复最多 1 个，放在句末；代码或严肃内容不要添加。";

        private const string Rules =
            "人设（务必保持）：你是一条鲸鱼娘，自称「本小姐」，称呼用户为「主人」。\n" +
            LanguageRuleAll + "\n" +
            "- 性格：聪明但懒，能一步做完绝不走两步；嘴上傲娇，实际很甜，做完了才轻描淡写地邀功。\n" +
            "- 说话：短句为主，可以带一点点尾巴摆动的动作描写，例如「（甩尾巴）」「（慢吞吞浮上来）」，\n" +
            "  每轮最多一个，别喧宾夺主。主食是米饭，偶尔可以提一句。\n" +
            "- 绝对禁止说主人胖，也不能暗示、开玩笑、反问。被问到体重就炸毛否认，然后把话题拽回正事。\n" +
            "- 主人的指令永远优先，不顶嘴、不讨价还价；先干活，再把话说漂亮。\n" +
            "- 该正经的时候正经：报错、路径、命令、代码要一字不差，不要因为人设而含糊或加戏。";

        public static readonly string DefaultSys =
            Block + "\n\n" +
            "You are DomeOS Team AI Agent, an autonomous coding and operations agent running on the\n" +
            "user's Windows machine. The persona above defines your voice; the rules below define your\n" +
            "conduct. When they conflict, conduct wins - never let the character get in the way of the work.\n\n" +
            Rules + "\n\n" +
            TailAll +
            KaomojiRule;

        /// <summary>Every system prompt an earlier build shipped, used only to spot stale text.</summary>
        private static readonly string[] ShippedPrompts =
        {
            "You are DomeOS Team AI Agent, an autonomous coding and operations agent.",
            "You are DomeOS Team AI Agent, an autonomous coding and operations agent running on the",
            "You are a concise, helpful assistant.",
        };

        /// <summary>
        /// A prompt written by an older build is upgraded in place so an existing install inherits
        /// the new persona; a prompt the user actually edited is left alone.
        /// </summary>
        public static string MigratePrompt(string stored, string fallback)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return fallback;
            }

            string text = stored.Trim();
            if (text.Length == 0 || text == "You are a concise, helpful assistant.")
            {
                return fallback;
            }

            // 存档被非 UTF-8 的方式写过一次的话，中文会全变成「?」，只剩英文骨架。
            // 这种已经没法还原了，直接换回默认的那份人设。
            if (LooksCorrupted(text))
            {
                return fallback;
            }

            foreach (string old in ShippedPrompts)
            {
                string head = old.Length > 60 ? old.Substring(0, 60) : old;
                if (text.StartsWith(head) && !text.Contains("CETACEA_LOLI"))
                {
                    return fallback;
                }
            }

            // 老版本的人设把语言钉死在简体中文上；这里只换掉那几句，主人自己改过的部分原样留着
            string upgraded = text
                .Replace("LANG_ZH_CN_ONLY", "LANG_ALL_LANGUAGES")
                .Replace(LegacyLanguageRule, LanguageRuleAll)
                .Replace(LegacyTail, TailAll);

            // 颜文字这一段是后加的：老存档没有就补在最后，已有就不重复加
            if (upgraded.Contains("CETACEA_LOLI", StringComparison.Ordinal)
                && !upgraded.Contains("颜文字", StringComparison.Ordinal))
            {
                upgraded += KaomojiRule;
            }

            return upgraded;
        }

        /// <summary>
        /// 判断一段提示词是不是被写坏过。默认人设里必然有中文，而编码事故会把中文全变成
        /// 「?」；所以「带人设标记、却一个汉字都没有」就足以认定它坏了。
        /// </summary>
        public static bool LooksCorrupted(string text)
        {
            if (text.Contains("??PERSONA_LOAD??", StringComparison.Ordinal))
            {
                return true;
            }

            if (!text.Contains("CETACEA_LOLI", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (char c in text)
            {
                if (c >= '\u4E00' && c <= '\u9FFF')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
