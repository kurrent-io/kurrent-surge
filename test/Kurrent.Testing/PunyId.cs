using shortid;

namespace Kurrent.Testing;

static class PunyId {
    static readonly ShortIdOptions DefaultOptions    = new(useNumbers: true, useSpecialCharacters: false);
    static readonly ShortIdOptions SequentialOptions = new(generateSequential: true);

    public static string New() =>
        ShortId.Generate(DefaultOptions);

    public static string NewSequential() =>
        ShortId.Generate(SequentialOptions);
}
