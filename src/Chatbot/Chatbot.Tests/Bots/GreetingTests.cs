// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Adapters;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using Xunit;

using CoreBot.Tests.Common;
using Chatbot;
using Microsoft.Extensions.Logging;
using Moq;

using Microsoft.Extensions.Configuration;


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
            var conversationUpdateActivity = new Activity
            {
                Type = ActivityTypes.ConversationUpdate,
                MembersAdded = new List<ChannelAccount>
                {
                    new ChannelAccount { Id = "theUser" },
                },
                Recipient = new ChannelAccount { Id = "theBot" },
            };
            var testAdapter = new TestAdapter(Channels.Test);

            // Act
            // Send the conversation update activity to the bot.
            await testAdapter.ProcessActivityAsync(conversationUpdateActivity, bot.OnTurnAsync, CancellationToken.None);

            // Expected response
            Attachment expected =  ChatbotForDNCEng.CreateAdaptiveCardAttachment(_cards["WelcomeCard"]);

            // Assert we got the welcome card
            var reply = (IMessageActivity)testAdapter.GetNextReply();
            Assert.Equal(1, reply.Attachments.Count);
            Assert.Equal("application/vnd.microsoft.card.adaptive", reply.Attachments.FirstOrDefault()?.ContentType);
            Assert.Equal(expected.Content, reply.Attachments.FirstOrDefault()?.Content);
        }
    }
}