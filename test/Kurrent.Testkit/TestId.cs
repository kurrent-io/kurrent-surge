using System.Security.Cryptography;

namespace Kurrent.Testkit;

public static class TestId {
    public static string New() =>
        RandomNumberGenerator.GetHexString(8, lowercase: true);
}
