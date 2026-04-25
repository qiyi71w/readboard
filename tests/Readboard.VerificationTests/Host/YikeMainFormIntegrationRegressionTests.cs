using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class YikeMainFormIntegrationRegressionTests
    {
        [Fact]
        public void MainForm_DefinesYikeRadioAndSyncTypeBindings()
        {
            string formSource = LoadSource("readboard", "Form1.cs");
            string designerSource = LoadSource("readboard", "Form1.Designer.cs");
            string configSource = LoadSource("readboard", "MainForm.Configuration.cs");

            Assert.Contains("private const int TYPE_YIKE = 6;", formSource);
            Assert.Contains("private YikeWindowContext lastYikeWindowContext = YikeWindowContext.Unknown();", formSource);
            Assert.Contains("return IsFoxSyncType(syncType) || syncType == TYPE_TYGEM || syncType == TYPE_SINA || syncType == TYPE_YIKE;", formSource);
            Assert.Contains("case TYPE_YIKE:", formSource);
            Assert.Contains("return SyncMode.Yike;", formSource);
            Assert.Contains("return ProtocolKeywords.Yike;", formSource);

            Assert.Contains("this.rdoYike = new System.Windows.Forms.RadioButton();", designerSource);
            Assert.Contains("this.groupBox1.Controls.Add(this.rdoYike);", designerSource);
            Assert.Contains("this.rdoYike.CheckedChanged += new System.EventHandler(this.radioButtonYike_CheckedChanged);", designerSource);
            Assert.Contains("private System.Windows.Forms.RadioButton rdoYike;", designerSource);

            Assert.Contains("case TYPE_YIKE:", configSource);
            Assert.Contains("rdoYike.Checked = true;", configSource);
        }

        [Fact]
        public void MainForm_RoutesYikeContextToCoordinatorAndTitle()
        {
            string formSource = LoadSource("readboard", "Form1.cs");
            string protocolSource = LoadSource("readboard", "MainForm.Protocol.cs");

            Assert.Contains("sessionCoordinator.SetYikeContext(lastYikeWindowContext);", formSource);
            Assert.Contains("MainWindowTitleFormatter.FormatYike(", formSource);
            Assert.Contains("getLangStr(\"MainForm_titleTagYike\")", formSource);
            Assert.Contains("YikeWindowContext.Unknown()", formSource);

            Assert.Contains("lastYikeWindowContext = YikeWindowContext.CopyOf(context);", protocolSource);
            Assert.Contains("sessionCoordinator.SetYikeContext(lastYikeWindowContext);", protocolSource);
            Assert.Contains("ApplyMainWindowTitle();", protocolSource);
        }

        [Fact]
        public void MainForm_LanguageResourcesExposeYikeLabels()
        {
            Assert.Contains("langItems[\"MainForm_rdoYike\"] = \"弈客\";", LoadSource("readboard", "Program.cs"));
            Assert.Contains("langItems[\"MainForm_titleTagYike\"] = \"弈客\";", LoadSource("readboard", "Program.cs"));
            Assert.Contains("MainForm_rdoYike=弈客", LoadSource("readboard", "language_cn.txt"));
            Assert.Contains("MainForm_titleTagYike=弈客", LoadSource("readboard", "language_cn.txt"));
            Assert.Contains("MainForm_rdoYike=Yike", LoadSource("readboard", "language_en.txt"));
            Assert.Contains("MainForm_titleTagYike=Yike", LoadSource("readboard", "language_en.txt"));
            Assert.Contains("MainForm_rdoYike=弈客", LoadSource("readboard", "language_jp.txt"));
            Assert.Contains("MainForm_titleTagYike=弈客", LoadSource("readboard", "language_jp.txt"));
            Assert.Contains("MainForm_rdoYike=Yike", LoadSource("readboard", "language_kr.txt"));
            Assert.Contains("MainForm_titleTagYike=Yike", LoadSource("readboard", "language_kr.txt"));
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }
    }
}
