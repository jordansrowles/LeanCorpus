namespace Rowles.LeanLucene.Analysis;

/// <summary>
/// Stop word lists for common languages.
/// </summary>
public static class StopWords
{
    /// <summary>
    /// Gets the classic 33-word English stop word list, equivalent to Lucene's
    /// <c>StopAnalyzer.ENGLISH_STOP_WORDS_SET</c>. This is the default used by
    /// <see cref="Analysis.Analysers.StandardAnalyser"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> English = StopWordFilter.DefaultStopWords;

    /// <summary>
    /// Gets the extended English stop word list (~95 words) which covers prepositions,
    /// pronouns, modals, adverbs, and negation fragments in addition to the classic set.
    /// Pass this to <c>IndexWriterConfig.StopWords</c> to opt in to more
    /// aggressive stop word removal.
    /// </summary>
    public static readonly IReadOnlyList<string> EnglishExtended = StopWordFilter.ExtendedStopWords;

    /// <summary>Gets the built-in French stop word list.</summary>
    public static readonly IReadOnlyList<string> French =
    [
        "au", "aux", "avec", "ce", "ces", "dans", "de", "des", "du",
        "elle", "en", "est", "et", "eux", "il", "je", "la", "le", "les",
        "leur", "lui", "ma", "mais", "me", "même", "mes", "mon", "ne",
        "nos", "notre", "nous", "on", "ou", "par", "pas", "pour",
        "qu", "que", "qui", "sa", "se", "ses", "son", "sont", "sur", "ta",
        "te", "tes", "toi", "ton", "tu", "un", "une", "vos", "votre",
        "vous", "c", "d", "j", "l", "m", "n", "s", "t", "y"
    ];

    /// <summary>Gets the built-in German stop word list.</summary>
    public static readonly IReadOnlyList<string> German =
    [
        "aber", "alle", "allem", "allen", "aller", "allerdings", "als",
        "also", "am", "an", "andere", "anderem", "anderen", "anderer",
        "anderes", "anderm", "andern", "anderr", "anders", "auch", "auf",
        "aus", "bei", "beim", "bin", "bis", "bist", "da", "damit",
        "dann", "das", "dass", "dem", "den", "denn", "der", "des",
        "die", "dies", "diese", "diesem", "diesen", "dieser", "dieses",
        "doch", "dort", "du", "durch", "ein", "eine", "einem", "einen",
        "einer", "einige", "einigem", "einigen", "einiger", "einiges",
        "er", "es", "etwas", "euch", "euer", "eure", "eurem",
        "euren", "eurer", "für", "hat", "hatte", "hier", "hin",
        "hinter", "ich", "ihm", "ihn", "ihnen", "ihr", "ihre",
        "ihrem", "ihren", "ihrer", "im", "in", "ist", "jede",
        "jedem", "jeden", "jeder", "jedes", "jene", "jenem", "jenen",
        "jener", "jenes", "kann", "kein", "keine", "keinem", "keinen",
        "keiner", "man", "mein", "meine", "meinem", "meinen",
        "meiner", "mit", "muss", "nach", "nicht", "noch", "nun",
        "nur", "ob", "oder", "ohne", "sehr", "sein", "seine",
        "seinem", "seinen", "seiner", "sich", "sie", "sind", "so",
        "soll", "und", "uns", "unser", "unsere", "unserem", "unseren",
        "unserer", "über", "um", "von", "vor", "war", "was",
        "weil", "welch", "welche", "welchem", "welchen", "welcher",
        "welches", "wenn", "wer", "wie", "wieder", "will", "wir",
        "wird", "wo", "zu", "zum", "zur"
    ];

    /// <summary>Gets the built-in Spanish stop word list.</summary>
    public static readonly IReadOnlyList<string> Spanish =
    [
        "a", "al", "algo", "algunas", "algunos", "ante", "antes", "como",
        "con", "contra", "cual", "cuando", "de", "del", "desde", "donde",
        "durante", "e", "el", "ella", "ellas", "ellos", "en", "entre",
        "era", "eras", "erais", "eran", "eres", "es", "esa", "esas",
        "ese", "eso", "esos", "esta", "estas", "este", "esto", "estos",
        "fue", "fueron", "fui", "fuimos", "ha", "han", "has", "hasta",
        "hay", "he", "hemos", "her", "ho", "hoy", "hubo", "la", "las",
        "le", "les", "lo", "los", "me", "mi", "mis", "mucho", "muchos",
        "muy", "más", "mí", "mía", "mías", "mío", "míos", "nada", "ni",
        "no", "nos", "nosotras", "nosotros", "nuestra", "nuestras",
        "nuestro", "nuestros", "o", "os", "otra", "otras", "otro",
        "otros", "para", "pero", "poco", "por", "porque", "que", "quien",
        "quienes", "qué", "se", "sea", "seáis", "seamos", "sean", "seas",
        "ser", "serás", "será", "seré", "seréis", "seremos", "serán",
        "si", "sin", "sobre", "son", "su", "sus", "también", "tanto",
        "te", "tenéis", "tenemos", "tener", "tengo", "ti", "tiene",
        "tienen", "tienes", "todo", "todos", "tu", "tus", "tú", "un",
        "una", "uno", "unos", "vosotras", "vosotros", "vuestra",
        "vuestras", "vuestro", "vuestros", "y", "ya", "yo"
    ];

    /// <summary>Gets the built-in Italian stop word list.</summary>
    public static readonly IReadOnlyList<string> Italian =
    [
        "a", "adesso", "ai", "al", "alla", "allo", "allora", "altre",
        "altri", "altro", "anche", "ancora", "avere", "aveva", "avevano",
        "che", "chi", "ci", "col", "come", "con", "contro", "cui",
        "da", "dagli", "dai", "dal", "dalla", "dalle", "dallo", "degli",
        "dei", "del", "dell", "della", "delle", "dello", "dentro", "di",
        "dietro", "dopo", "dove", "due", "durante", "e", "ebbe", "ebbero",
        "ed", "era", "erano", "essere", "fa", "fare", "fin", "fino",
        "fra", "fu", "fui", "fummo", "fuori", "furono", "gli", "ha",
        "hai", "hanno", "ho", "i", "il", "in", "io", "l", "la", "le",
        "lei", "li", "lo", "loro", "lui", "ma", "mi", "mia", "mie",
        "miei", "mio", "nella", "nelle", "nello", "noi", "non", "nostro",
        "o", "ogni", "oltre", "per", "perché", "poi", "prima", "pure",
        "però", "qui", "questa", "queste", "questi", "quello", "quelli",
        "quella", "quello", "se", "sei", "siamo", "siete", "solo",
        "sono", "su", "sua", "sue", "sugli", "sui", "sul", "sulla",
        "sulle", "sullo", "suoi", "suo", "te", "ti", "tra", "tu", "tua",
        "tue", "tuoi", "tuo", "tutti", "tutto", "un", "una", "uno",
        "verso", "vi", "voi", "vostra", "vostre", "vostri", "vostro"
    ];

    /// <summary>Gets the built-in Portuguese stop word list.</summary>
    public static readonly IReadOnlyList<string> Portuguese =
    [
        "a", "ao", "aos", "aquela", "aquelas", "aquele", "aqueles",
        "aquilo", "as", "até", "com", "como", "da", "das", "de", "dela",
        "delas", "dele", "deles", "depois", "do", "dos", "e", "ela",
        "elas", "ele", "eles", "em", "entre", "era", "eram", "essa",
        "essas", "esse", "esses", "esta", "estas", "este", "estes",
        "eu", "foi", "fomos", "for", "foram", "fora", "há", "isso",
        "isto", "já", "lhe", "lhes", "lo", "mais", "mas", "me",
        "mesmo", "meu", "meus", "minha", "minhas", "muito", "na",
        "nas", "nem", "no", "nos", "nós", "nossa", "nossas", "nosso",
        "nossos", "num", "numa", "o", "os", "ou", "para", "pela",
        "pelas", "pelo", "pelos", "por", "qual", "quando", "que",
        "quem", "se", "sem", "ser", "seu", "seus", "só", "sua",
        "suas", "também", "te", "tem", "ter", "teu", "teus", "tua",
        "tuas", "tudo", "um", "uma", "você", "vocês", "vos", "à",
        "às", "é", "são"
    ];

    /// <summary>Gets the built-in Dutch stop word list.</summary>
    public static readonly IReadOnlyList<string> Dutch =
    [
        "aan", "al", "alles", "als", "altijd", "andere", "ben", "bij",
        "daar", "dan", "dat", "de", "der", "deze", "die", "dit", "doch",
        "doen", "door", "dus", "een", "eens", "en", "er", "ge", "geen",
        "geweest", "haar", "had", "heb", "hebben", "heeft", "hem",
        "het", "hier", "hij", "hoe", "hun", "iemand", "iets", "ik",
        "in", "is", "ja", "je", "kan", "komen", "kunnen", "maar",
        "me", "meer", "men", "met", "mij", "mijn", "moet", "na",
        "naar", "niet", "niets", "nog", "nu", "of", "om", "omdat",
        "onder", "ons", "ook", "op", "over", "reeds", "te", "tegen",
        "toch", "toen", "tot", "u", "uit", "uw", "van", "veel",
        "voor", "want", "waren", "was", "wat", "we", "werd", "wezen",
        "wie", "wij", "wil", "worden", "wordt", "ze", "zei", "zelf",
        "zich", "zij", "zijn", "zo", "zonder", "zou"
    ];

    /// <summary>Gets the built-in Russian stop word list.</summary>
    public static readonly IReadOnlyList<string> Russian =
    [
        "а", "без", "более", "бы", "был", "была", "были", "было",
        "быть", "в", "вам", "вас", "весь", "во", "вот", "все",
        "всего", "всех", "вы", "где", "да", "даже", "для", "до",
        "его", "ее", "если", "есть", "еще", "же", "за", "здесь",
        "и", "из", "или", "им", "их", "к", "как", "ко", "когда",
        "кто", "ли", "либо", "мне", "может", "мой", "моя", "мы",
        "на", "надо", "наш", "не", "него", "нее", "нет", "ни",
        "них", "но", "ну", "о", "об", "однако", "он", "она",
        "они", "оно", "от", "по", "под", "при", "с", "со",
        "так", "также", "такой", "там", "те", "тем", "то",
        "того", "тоже", "той", "только", "том", "ты", "у",
        "уже", "хотя", "чего", "чей", "чем", "что", "чтобы",
        "чье", "чья", "эта", "эти", "это", "я"
    ];

    /// <summary>Gets the built-in Arabic stop word list.</summary>
    public static readonly IReadOnlyList<string> Arabic =
    [
        "من", "في", "على", "إلى", "عن", "مع", "هذا", "هذه", "ذلك",
        "تلك", "هو", "هي", "هم", "هن", "أنا", "أنت", "أنتم", "نحن",
        "كان", "كانت", "كانوا", "يكون", "تكون", "قد", "قال", "قالت",
        "لا", "لم", "لن", "ليس", "إن", "أن", "لأن", "حتى", "إذا",
        "عند", "بعد", "قبل", "حين", "ثم", "أو", "و", "ف", "ب", "ل",
        "كل", "بعض", "غير", "أكثر", "أقل", "جدا", "فقط", "حيث",
        "كيف", "متى", "أين", "ما", "من", "لماذا", "التي", "الذي",
        "الذين", "اللواتي", "اللاتي", "كما", "لكن", "بل", "إلا"
    ];

    /// <summary>Gets the built-in Chinese stop word list.</summary>
    public static readonly IReadOnlyList<string> Chinese =
    [
        "的", "了", "在", "是", "我", "有", "和", "就", "不", "人",
        "都", "一", "一个", "上", "也", "很", "到", "说", "要", "去",
        "你", "会", "着", "没有", "看", "好", "自己", "这", "那", "它",
        "他", "她", "我们", "你们", "他们", "她们", "什么", "这个",
        "那个", "这些", "那些", "但", "但是", "因为", "所以", "如果",
        "虽然", "然后", "还", "只", "就是", "已经", "可以", "对", "把",
        "被", "从", "用", "而", "与", "及", "或", "并", "啊", "吧",
        "呢", "吗", "嗯", "哦"
    ];

    /// <summary>Gets the built-in Japanese stop word list.</summary>
    public static readonly IReadOnlyList<string> Japanese =
    [
        // Particles
        "は", "が", "を", "に", "で", "と", "も", "の", "へ", "や",
        "か", "な", "ね", "よ", "わ", "ぞ", "ぜ", "さ", "ら", "て",
        "から", "まで", "より", "ので", "のに", "けど", "けれど",
        "ながら", "ところ", "ため", "だけ", "しか", "ほど", "など",
        // Copulas & auxiliaries
        "だ", "です", "ます", "ない", "ぬ", "ん", "たい", "らしい",
        "ようだ", "そうだ", "だろう", "でしょう", "かも", "はず",
        // Common verbs used as function words
        "いる", "ある", "する", "なる", "できる", "もらう", "あげる",
        "くれる", "おく", "みる", "しまう", "いく", "くる",
        // Pronouns & demonstratives
        "これ", "それ", "あれ", "この", "その", "あの", "ここ", "そこ",
        "あそこ", "私", "僕", "俺", "彼", "彼女", "あなた", "君",
        // Conjunctions & adverbs
        "また", "そして", "しかし", "でも", "だが", "ただ", "また",
        "さらに", "それに", "なお", "つまり", "すなわち", "なぜなら",
        "もし", "たとえ", "ように", "ために", "について", "として",
        // Numbers / counters used as function words
        "一", "二", "三", "何", "どの", "どれ", "どこ", "誰", "いつ",
        "どう", "なぜ", "いくつ", "いくら"
    ];

    /// <summary>Gets the built-in Korean stop word list.</summary>
    public static readonly IReadOnlyList<string> Korean =
    [
        // Particles
        "이", "가", "을", "를", "은", "는", "의", "에", "에서",
        "로", "으로", "와", "과", "도", "만", "까지", "부터",
        "에게", "한테", "께", "처럼", "보다", "마다", "조차",
        // Copulas & endings
        "이다", "입니다", "이에요", "예요", "았", "었", "겠",
        "ㄴ다", "는다", "ㄹ다", "지", "고", "며", "면",
        // Common pronouns & demonstratives
        "나", "저", "너", "그", "그녀", "우리", "저희",
        "이것", "그것", "저것", "여기", "거기", "저기",
        // Common adverbs & conjunctions
        "그리고", "그러나", "하지만", "그래서", "따라서",
        "그런데", "또한", "또", "아직", "이미", "정말",
        "매우", "너무", "좀", "더", "덜", "가장", "모두",
        // Common verbs used as function words
        "있다", "없다", "하다", "되다", "같다", "보다"
    ];

    /// <summary>
    /// Gets the stop word list for a BCP 47 language code, or <see langword="null"/>
    /// if the language is not supported.
    /// </summary>
    /// <param name="languageCode">
    /// A BCP 47 language tag, e.g. <c>"en"</c>, <c>"fr"</c>, <c>"zh"</c>.
    /// Region subtags are ignored — <c>"pt-BR"</c> resolves to <see cref="Portuguese"/>.
    /// </param>
    public static IReadOnlyList<string>? ForLanguage(string languageCode)
    {
        // Normalise: strip region subtag so "pt-BR", "zh-Hans", etc. resolve cleanly.
        var tag = languageCode.Split('-')[0].ToLowerInvariant();

        return tag switch
        {
            "en" => English,
            "fr" => French,
            "de" => German,
            "es" => Spanish,
            "it" => Italian,
            "pt" => Portuguese,
            "nl" => Dutch,
            "ru" => Russian,
            "ar" => Arabic,
            "zh" => Chinese,
            "ja" => Japanese,
            "ko" => Korean,
            _ => null
        };
    }

    /// <summary>
    /// Returns all supported BCP 47 language codes.
    /// </summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } =
    [
        "en", "fr", "de", "es", "it", "pt", "nl", "ru", "ar", "zh", "ja", "ko"
    ];
}
