using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Color = System.Drawing.Color;
using File = System.IO.File;

namespace TelegramImageMergeBot
{
    class Program
    {
        static Dictionary<long, List<string>> userImages = new();
        static Dictionary<long, bool> waitingForLogo = new();

        static async Task Main()
        {
            string botToken = "7654954725:AAE8WgR6DwlGt8xKdjVLj4gDqKw9eE18xO0"; // Bot tokeningizni bu yerga yozing
            var botClient = new TelegramBotClient(botToken);

            using var cts = new CancellationTokenSource();

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            botClient.StartReceiving(
                async (bot, update, token) => await HandleUpdateAsync(bot, update, token, botToken),
                HandleErrorAsync,
                receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();
            Console.WriteLine($"✅ Bot ishga tushdi: @{me.Username}");
            Console.ReadLine();
        }

        static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken, string botToken)
        {
            if (update.Message is not { Photo: not null }) return;

            var message = update.Message;
            long userId = message.From.Id;
            var fileId = message.Photo.Last().FileId;

            string filePath = $"{userId}_{Guid.NewGuid()}.png";

            // Rasmni yuklab olish
            var file = await bot.GetFileAsync(fileId, cancellationToken);
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                using var httpClient = new HttpClient();
                var fileUrl = $"https://api.telegram.org/file/bot{botToken}/{file.FilePath}";
                using var response = await httpClient.GetAsync(fileUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            if (waitingForLogo.TryGetValue(userId, out var isWaiting) && isWaiting)
            {
                // Bu logo rasmi — final image yasaymiz
                string mergedPath = CombineWithLogo(userImages[userId][0], userImages[userId][1], filePath, userId);

                using var output = File.OpenRead(mergedPath);
                await bot.SendPhotoAsync(
                    chatId: message.Chat.Id,
                    photo: new InputFileStream(output),
                    caption: "✅ Tayyor rasm",
                    cancellationToken: cancellationToken
                );

                // Tozalash
                foreach (var img in userImages[userId])
                    File.Delete(img);
                File.Delete(filePath);
                File.Delete(mergedPath);

                userImages[userId].Clear();
                waitingForLogo[userId] = false;
            }
            else
            {
                if (!userImages.ContainsKey(userId))
                    userImages[userId] = new List<string>();

                userImages[userId].Add(filePath);

                if (userImages[userId].Count == 2)
                {
                    await bot.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "✅ Endi logotip/reklama rasmni yuboring (PNG formatda)",
                        cancellationToken: cancellationToken
                    );
                    waitingForLogo[userId] = true;
                }
                else
                {
                    await bot.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "✅ Yana 1 ta rasm yuboring...",
                        cancellationToken: cancellationToken
                    );
                }
            }
        }

        static string CombineWithLogo(string path1, string path2, string logoPath, long userId)
        {
            using var img1 = Image.FromFile(path1);
            using var img2 = Image.FromFile(path2);
            using var logo = Image.FromFile(logoPath);

            // Resize rasm balandlik bo‘yicha
            int targetHeight = Math.Min(img1.Height, img2.Height);
            int w1 = img1.Width * targetHeight / img1.Height;
            int w2 = img2.Width * targetHeight / img2.Height;

            using var resized1 = new Bitmap(img1, new Size(w1, targetHeight));
            using var resized2 = new Bitmap(img2, new Size(w2, targetHeight));

            int totalWidth = resized1.Width + resized2.Width;
            using var combined = new Bitmap(totalWidth, targetHeight);
            using (var g = Graphics.FromImage(combined))
            {
                g.Clear(Color.White);
                g.DrawImage(resized1, 0, 0);
                g.DrawImage(resized2, resized1.Width, 0);
            }

            // Kvadratga kesish
            int square = Math.Min(combined.Width, combined.Height);
            int x = (combined.Width - square) / 2;
            int y = (combined.Height - square) / 2;
            var final = combined.Clone(new Rectangle(x, y, square, square), combined.PixelFormat);

            // Logo: dumaloq qilib kesish va 50% shaffoflik bilan markazga joylash
            using var circleLogo = CropToCircle(logo, 0.5f); // 0.5 — shaffoflik
            using var finalGraphics = Graphics.FromImage(final);
            int logoSize = (int)(square / 1.8);
            int lx = (square - logoSize) / 2;
            int ly = (square - logoSize) / 2;
            finalGraphics.DrawImage(circleLogo, lx, ly, logoSize, logoSize);

            string outputPath = $"{userId}_final.jpg";
            final.Save(outputPath, ImageFormat.Jpeg);
            return outputPath;
        }

        static Bitmap CropToCircle(Image image, float opacity)
        {
            int size = Math.Min(image.Width, image.Height);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);

            var path = new GraphicsPath();
            path.AddEllipse(0, 0, size, size);
            g.SetClip(path);
            g.Clear(Color.Transparent);

            var attr = new ImageAttributes();
            var cm = new ColorMatrix
            {
                Matrix33 = opacity // shaffoflik darajasi
            };
            attr.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

            g.DrawImage(image, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, attr);
            return bmp;
        }

        static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var error = exception switch
            {
                ApiRequestException apiEx => $"Telegram API xatosi: {apiEx.Message}",
                _ => exception.ToString()
            };
            Console.WriteLine(error);
            return Task.CompletedTask;
        }
    }
}
