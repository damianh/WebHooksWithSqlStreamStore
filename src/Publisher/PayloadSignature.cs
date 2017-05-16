namespace WebHooks.Publisher
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    public static class PayloadSignature
    {
        public static string CreateSignature(string payload, string secret)
        {
            if (!string.IsNullOrWhiteSpace(secret))
            {
                var keyByte = Encoding.UTF8.GetBytes(secret);
                var messageBytes = Encoding.UTF8.GetBytes(payload);
                using (var hmacsha256 = new HMACSHA1(keyByte))
                {
                    var hash = hmacsha256.ComputeHash(messageBytes);
                    return Convert.ToBase64String(hash);
                }
            }
            return string.Empty;
        }
    }
}