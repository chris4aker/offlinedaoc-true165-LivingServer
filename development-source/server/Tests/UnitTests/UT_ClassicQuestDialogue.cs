using System.Collections.Generic;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Owner 2026-11-05: Kaere's [get it for me] went nowhere; recorded dialogue with a dead branch.</summary>
    [TestFixture]
    public sealed class UT_ClassicQuestDialogue
    {
        private static readonly Dictionary<string, string> Kaere = new()
        {
            ["to aid Lady Treva"] = "... could you [get it for me]? [Will you gather the components for Keare's spell to protect Anniogel?]",
            ["Will you gather the components for Keare's spell to protect Anniogel?"] = "All the components for the potion [can be found] on creatures.",
            ["my ingredient"] = "Do you [need directions] to the creatures you must hunt or do you [know already] where they are?",
            ["know already"] = "Here is the vial. Remember, you may obtain the [components] in any order.",
        };

        [Test]
        public void AClickedQuestionFindsItsRecordedLineWithoutTheQuestionMark() =>
            Assert.That(DOL.GS.Quests.ClassicQuests.FindLine(Kaere, "Will you gather the components for Keare's spell to protect Anniogel"),
                Does.StartWith("All the components"));

        [Test]
        public void ADeadBranchAnswersWithTheOtherBranchOfTheSameLine()
        {
            Assert.That(DOL.GS.Quests.ClassicQuests.FindLine(Kaere, "get it for me"), Is.Null);
            Assert.That(DOL.GS.Quests.ClassicQuests.SiblingReply(Kaere, "get it for me"), Does.StartWith("All the components"));
            Assert.That(DOL.GS.Quests.ClassicQuests.SiblingReply(Kaere, "need directions"), Does.StartWith("Here is the vial"));
        }

        [Test]
        public void AKeywordNobodySaysStaysUnanswered() =>
            Assert.That(DOL.GS.Quests.ClassicQuests.SiblingReply(Kaere, "components"), Is.Null);
    }
}
