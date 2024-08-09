// Based off Bot Builder V4 SDK Template for Visual Studio CoreBot v4.22.0

//using Chatbot.Tests.Bots;
//using Chatbot.Tests.Common;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatbot.Tests.Bots
{
    public class GreetingTests
    {
        private readonly Dictionary<string, string> _cards = new Dictionary<string, string>()
        {
            {"FeedbackCard", @"C:\Users\t-calikuang\source\repos\dnceng\src\Chatbot\Chatbot\Resources\FeedbackCard.json"},
            {"ContactSheet", @"C:\Users\t-calikuang\source\repos\dnceng\src\Chatbot\Chatbot\Resources\ContactSheet.json"},
            {"WelcomeCard", @"C:\Users\t-calikuang\source\repos\dnceng\src\Chatbot\Chatbot\Resources\WelcomeCard.json"}
        };

        [Fact]
        public async Task TestGreetingSent()
        {
            // Set up: Mock telemetry client, configuration, turn context
            Mock<ILogger<ChatbotForDNCEng>> mockLogger = new Mock<ILogger<ChatbotForDNCEng>>();
            Mock<IConfiguration> mockConfiguration = new Mock<IConfiguration>();
            Mock<ITurnContext> mockTurnContext = new Mock<ITurnContext>();
            Activity activity = new Activity
            {
                Type = ActivityTypes.ConversationUpdate,
                MembersAdded = new List<ChannelAccount> { new ChannelAccount() { Id="user1", Name="User one", Role="user"} },
                Recipient = new ChannelAccount() { Id = "bot1", Name = "Bot one", Role = "bot" }
            };
            mockTurnContext.Setup(tc => tc.Activity).Returns(activity);

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            ChatbotForDNCEng bot = new ChatbotForDNCEng(mockLogger.Object, mockConfiguration.Object);

            // Expected value
            Attachment expectedCard = ChatbotForDNCEng.CreateAdaptiveCardAttachment(_cards["WelcomeCard"]);

            // Act
            // Send the conversation update activity to the bot.
            //await bot.SendWelcomeMessageAsync(mockTurnContext.Object, cancellationToken);
            await bot.OnTurnAsync(mockTurnContext.Object, cancellationToken);

            // Verify
            // I think it should be twice, once for the bot to enter the channel and once for the user
            var expectedActivity = MessageFactory.Attachment(expectedCard);
            mockTurnContext.Verify(context => context.SendActivityAsync(expectedActivity,
            It.IsAny<CancellationToken>()), Times.Once);


        }
    }
}