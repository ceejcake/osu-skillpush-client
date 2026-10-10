// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Skinning;

namespace osu.Game.Tests.NonVisual.Skinning
{
    [TestFixture]
    public class SkinCursorSizeMemoryTest
    {
        private Bindable<string> skin = null!;
        private BindableFloat cursorSize = null!;
        private Bindable<string> saved = null!;

        [SetUp]
        public void SetUp()
        {
            skin = new Bindable<string>("a");
            cursorSize = new BindableFloat(1) { MinValue = 0.1f, MaxValue = 2 };
            saved = new Bindable<string>(string.Empty);
        }

        [Test]
        public void TestSizeRestoredWhenSwitchingBack()
        {
            using var memory = new SkinCursorSizeMemory(skin, cursorSize, saved);

            cursorSize.Value = 0.7f;

            skin.Value = "b";
            cursorSize.Value = 1.4f;

            skin.Value = "a";
            Assert.That(cursorSize.Value, Is.EqualTo(0.7f));

            skin.Value = "b";
            Assert.That(cursorSize.Value, Is.EqualTo(1.4f));
        }

        [Test]
        public void TestNewSkinKeepsCurrentSize()
        {
            using var memory = new SkinCursorSizeMemory(skin, cursorSize, saved);

            cursorSize.Value = 0.7f;
            skin.Value = "never used";

            Assert.That(cursorSize.Value, Is.EqualTo(0.7f));

            // And that doesn't disturb what the first skin had.
            cursorSize.Value = 1.2f;
            skin.Value = "a";
            Assert.That(cursorSize.Value, Is.EqualTo(0.7f));
        }

        [Test]
        public void TestSurvivesRestart()
        {
            using (new SkinCursorSizeMemory(skin, cursorSize, saved))
            {
                cursorSize.Value = 0.7f;
                skin.Value = "b";
                cursorSize.Value = 1.4f;
            }

            using (new SkinCursorSizeMemory(skin, cursorSize, saved))
            {
                Assert.That(cursorSize.Value, Is.EqualTo(1.4f));

                skin.Value = "a";
                Assert.That(cursorSize.Value, Is.EqualTo(0.7f));
            }
        }

        [Test]
        public void TestUnreadableSaveIsIgnored()
        {
            saved.Value = "not json";

            using var memory = new SkinCursorSizeMemory(skin, cursorSize, saved);

            cursorSize.Value = 0.7f;
            skin.Value = "b";
            skin.Value = "a";

            Assert.That(cursorSize.Value, Is.EqualTo(0.7f));
        }
    }
}
