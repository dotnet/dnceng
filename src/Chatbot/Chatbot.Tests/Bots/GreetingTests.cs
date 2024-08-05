// Based off Bot Builder V4 SDK Template for Visual Studio CoreBot v4.22.0

using ChatbotTests.Bots;
using ChatbotTests.Tests.Common;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Adapters;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatbot.Tests.Bots
{
    public class GreetingTests
    {
        [Fact]
        public void TestHelloWorldString()
        {
            string expected = "Hello world";
            string actual = "Hello world";
            Assert.Equal(expected, actual);
        }
    }
}