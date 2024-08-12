// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Chatbot;
using Microsoft.Bot.Builder.Adapters;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;


namespace CoreBot.Tests.Bots
{
    public class DialogAndWelcomeBotTests
    {
        private readonly Dictionary<string, string> _cards = new Dictionary<string, string>()
        {
            {"FeedbackCard", @"C:\Users\t-calikuang\source\repos\dnceng\src\Chatbot\Chatbot\Resources\FeedbackCard.json"},
            {"ContactSheet", @"C:\Users\t-calikuang\source\repos\dnceng\src\Chatbot\Chatbot\Resources\ContactSheet.json"},
            {"WelcomeCard", @"C:\Users\t-calikuang\source\repos\dnceng\src\Chatbot\Chatbot\Resources\WelcomeCard.json"}
        };

        [Fact]
        public async Task ReturnsWelcomeCardOnConversationUpdate()
        {
            // Arrange
            Mock<ILogger<ChatbotForDNCEng>> mockLogger = new Mock<ILogger<ChatbotForDNCEng>>();
            Mock<IConfiguration> mockConfiguration = new Mock<IConfiguration>();
            ChatbotForDNCEng bot = new ChatbotForDNCEng(mockLogger.Object, mockConfiguration.Object);

            // Create conversationUpdate activity
            Activity conversationUpdateActivity = new Activity
            {
                Type = ActivityTypes.ConversationUpdate,
                MembersAdded = new List<ChannelAccount>
                {
                    new ChannelAccount { Id = "theUser" },
                },
                Recipient = new ChannelAccount { Id = "theBot" },
            };
            TestAdapter testAdapter = new TestAdapter(Channels.Test);

            // Act
            // Send the conversation update activity to the bot.
            await testAdapter.ProcessActivityAsync(conversationUpdateActivity, bot.OnTurnAsync, CancellationToken.None);

            // Expected response
            Attachment expected = ChatbotForDNCEng.CreateAdaptiveCardAttachment(_cards["WelcomeCard"]);

            // Assert we got the welcome card
            IMessageActivity reply = (IMessageActivity)testAdapter.GetNextReply();
            Assert.Equal(1, reply.Attachments.Count);
            Assert.Equal("application/vnd.microsoft.card.adaptive", reply.Attachments.FirstOrDefault()?.ContentType);
            Assert.Equal(expected.Content, reply.Attachments.FirstOrDefault()?.Content);

            // Assert we got Suggested Actions
            SuggestedActions expectedActions = ChatbotForDNCEng.CreateSuggestedActions();
            Assert.IsType<SuggestedActions>(reply.SuggestedActions);
            Assert.Equal(expectedActions.Actions.Count, reply.SuggestedActions.Actions.Count);
        }

        [Theory]
        [InlineData("Feedback", "FeedbackCard")]
        [InlineData("Contact", "ContactSheet")]
        public async Task ReturnsCorrectCard(string userMessage, string expectdCard)
        {
            // Arrange
            Mock<ILogger<ChatbotForDNCEng>> mockLogger = new Mock<ILogger<ChatbotForDNCEng>>();
            Mock<IConfiguration> mockConfiguration = new Mock<IConfiguration>();
            ChatbotForDNCEng bot = new ChatbotForDNCEng(mockLogger.Object, mockConfiguration.Object);

            TestAdapter testAdapter = new TestAdapter(Channels.Test);

            // Act
            // Send the conversation update activity to the bot.
            await testAdapter.SendTextToBotAsync(userMessage, bot.OnTurnAsync, CancellationToken.None);

            // Expected response
            Attachment expected = ChatbotForDNCEng.CreateAdaptiveCardAttachment(_cards[expectdCard]);

            // Assert we got the feedback card
            IMessageActivity reply = (IMessageActivity)testAdapter.GetNextReply();
            Assert.Equal(1, reply.Attachments.Count);
            Assert.Equal("application/vnd.microsoft.card.adaptive", reply.Attachments.FirstOrDefault()?.ContentType);
            Assert.Equal(expected.Content, reply.Attachments.FirstOrDefault()?.Content);

            // Assert we got Suggested Actions
            reply = (IMessageActivity)testAdapter.GetNextReply();
            SuggestedActions expectedActions = ChatbotForDNCEng.CreateSuggestedActions();
            Assert.IsType<SuggestedActions>(reply.SuggestedActions);
            Assert.Equal(expectedActions.Actions.Count, reply.SuggestedActions.Actions.Count);
        }

        [Theory]
        [InlineData("What is Known issues?")]
        [InlineData("How do I use Helix job sender?")]
        public async Task ReturnsAIResponse(string userMessage)
        {
            // Arrange
            Mock<ILogger<ChatbotForDNCEng>> mockLogger = new Mock<ILogger<ChatbotForDNCEng>>();
            Mock<IConfiguration> mockConfiguration = new Mock<IConfiguration>();
            mockConfiguration.Setup(x => x["KeyVaultName"]).Returns("DncengChatbotKV");
            mockConfiguration.Setup(x => x["MicrosoftAppId"]).Returns("ed9b4931-f097-4f9b-ae95-cec8c7f06dd8");
            ChatbotForDNCEng bot = new ChatbotForDNCEng(mockLogger.Object, mockConfiguration.Object);

            TestAdapter testAdapter = new TestAdapter(Channels.Test);

            // Act
            // Send the conversation update activity to the bot.
            await testAdapter.SendTextToBotAsync(userMessage, bot.OnTurnAsync, CancellationToken.None);

            // Expected response
            string notExpected = "The requested information is not available in the retrieved data. Please try another query or topic.";

            // Assert we got the welcome card
            IMessageActivity reply = (IMessageActivity)testAdapter.GetNextReply();
            Assert.Equal(1, reply.Attachments.Count);
            Assert.Equal("application/vnd.microsoft.card.adaptive", reply.Attachments.FirstOrDefault()?.ContentType);
            Assert.DoesNotContain(notExpected, reply.Attachments.FirstOrDefault()?.Content.ToString());

            // Assert we got Suggested Actions
            reply = (IMessageActivity)testAdapter.GetNextReply();
            SuggestedActions expectedActions = ChatbotForDNCEng.CreateSuggestedActions();
            Assert.IsType<SuggestedActions>(reply.SuggestedActions);
            Assert.Equal(expectedActions.Actions.Count, reply.SuggestedActions.Actions.Count);
        }

        [Theory]
        [InlineData("Who is Abraham Lincoln?")]
        [InlineData("How do I make cheese at home?")]
        public async Task ReturnsCanNotFindResponse(string userMessage)
        {
            // Arrange
            Mock<ILogger<ChatbotForDNCEng>> mockLogger = new Mock<ILogger<ChatbotForDNCEng>>();
            Mock<IConfiguration> mockConfiguration = new Mock<IConfiguration>();
            mockConfiguration.Setup(x => x["KeyVaultName"]).Returns("DncengChatbotKV");
            mockConfiguration.Setup(x => x["MicrosoftAppId"]).Returns("ed9b4931-f097-4f9b-ae95-cec8c7f06dd8");
            ChatbotForDNCEng bot = new ChatbotForDNCEng(mockLogger.Object, mockConfiguration.Object);

            TestAdapter testAdapter = new TestAdapter(Channels.Test);

            // Act
            // Send the conversation update activity to the bot.
            await testAdapter.SendTextToBotAsync(userMessage, bot.OnTurnAsync, CancellationToken.None);

            // Expected response
            string expected = "The requested information is not available in the retrieved data. Please try another query or topic.";

            // Assert we got the welcome card
            IMessageActivity reply = (IMessageActivity)testAdapter.GetNextReply();
            Assert.Equal(1, reply.Attachments.Count);
            Assert.Equal("application/vnd.microsoft.card.adaptive", reply.Attachments.FirstOrDefault()?.ContentType);
            Assert.Contains(expected, reply.Attachments.FirstOrDefault()?.Content.ToString());

            // Assert we got Suggested Actions
            reply = (IMessageActivity)testAdapter.GetNextReply();
            SuggestedActions expectedActions = ChatbotForDNCEng.CreateSuggestedActions();
            Assert.IsType<SuggestedActions>(reply.SuggestedActions);
            Assert.Equal(expectedActions.Actions.Count, reply.SuggestedActions.Actions.Count);
        }
    }
}