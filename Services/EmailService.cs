using DocumentFormat.OpenXml.Vml;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BIP_SMEMC.Services
{
    public class EmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            var host = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
            var port = int.TryParse(_configuration["Email:SmtpPort"], out var p) ? p : 587;
            var username = _configuration["Email:SmtpUsername"] ?? string.Empty;
            var password = _configuration["Email:SmtpPassword"] ?? string.Empty;
            var from = _configuration["Email:From"] ?? username;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Email SMTP credentials are missing.");
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SME Finance Assistant", from));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "SME Finance Assistant - Password Reset";
            message.Body = new TextPart("html")
            {
                Text = $"<p>Click the link below to reset your password:</p><p><a href=\"{resetLink}\">{resetLink}</a></p><p>This link expires in 30 minutes.</p>"
            };

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(username, password);
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);
        }

        public async Task SendTwoFactorCodeAsync(string toEmail, string code)
        {
            var host = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
            var port = int.TryParse(_configuration["Email:SmtpPort"], out var p) ? p : 587;
            var username = _configuration["Email:SmtpUsername"] ?? string.Empty;
            var password = _configuration["Email:SmtpPassword"] ?? string.Empty;
            var from = _configuration["Email:From"] ?? username;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Email SMTP credentials are missing.");
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SME Finance Assistant", from));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "SME Finance Assistant - Your 2FA Code";
            message.Body = new TextPart("plain")
            {
                Text = $"Your verification code is: {code}\nThis code expires in 5 minutes."
            };

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(username, password);
            await smtp.SendAsync(message);
            await smtp.DisconnectAsync(true);
        }
    }
}
