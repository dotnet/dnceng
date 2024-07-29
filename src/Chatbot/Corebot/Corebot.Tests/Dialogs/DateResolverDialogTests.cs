// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.22.0

using Corebot.Dialogs;
using Corebot.Tests.Common;
using Corebot.Tests.Dialogs.TestData;
using Microsoft.Bot.Builder.Testing;
using Microsoft.Bot.Builder.Testing.XUnit;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Schema;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Corebot.Tests.Dialogs
{
    public class DateResolverDialogTests : BotTestBase
    {
        public DateResolverDialogTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [MemberData(nameof(DateResolverDialogTestsDataGenerator.DateResolverCases), MemberType = typeof(DateResolverDialogTestsDataGenerator))]
        public async Task DialogFlowTests(TestDataObject testData)
        {
            // Arrange
            var testCaseData = testData.GetObject<DateResolverDialogTestCase>();
            var sut = new DateResolverDialog();
            var testClient = new DialogTestClient(Channels.Test, sut, testCaseData.InitialData, new[] { new XUnitDialogTestLogger(Output) });

            // Execute the test case
            Output.WriteLine($"Test Case: {testCaseData.Name}");
            Output.WriteLine($"\r\nDialog Input: {testCaseData.InitialData}");
            for (var i = 0; i < testCaseData.UtterancesAndReplies.GetLength(0); i++)
            {
                var reply = await testClient.SendActivityAsync<IMessageActivity>(testCaseData.UtterancesAndReplies[i, 0]);
                Assert.Equal(testCaseData.UtterancesAndReplies[i, 1], reply?.Text);
            }

            Output.WriteLine($"\r\nDialog result: {testClient.DialogTurnResult.Result}");
            Assert.Equal(testCaseData.ExpectedResult, testClient.DialogTurnResult.Result);
        }
    }
}
