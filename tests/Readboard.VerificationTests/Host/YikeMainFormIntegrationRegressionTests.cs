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
            Assert.DoesNotContain("sessionCoordinator.SetYikeContext(yikeWindowContext);", formSource);
            Assert.Contains("MainWindowTitleFormatter.FormatYike(", formSource);
            Assert.Contains("getLangStr(\"MainForm_titleTagYike\")", formSource);
            Assert.Contains("YikeWindowContext.Unknown()", formSource);

            Assert.Contains("lastYikeWindowContext = YikeWindowContext.CopyOf(context);", protocolSource);
            Assert.Contains("sessionCoordinator.SetYikeContext(lastYikeWindowContext);", protocolSource);
            Assert.Contains("ApplyMainWindowTitle();", protocolSource);
        }

        [Fact]
        public void MainForm_YikeContextHandlingIsSimplified()
        {
            string formSource = LoadSource("readboard", "Form1.cs");
            string protocolSource = LoadSource("readboard", "MainForm.Protocol.cs");
            string resolveMethod = GetMethodSlice(formSource, "private YikeWindowContext ResolveYikeWindowContext()");
            string setHandleMethod = GetMethodSlice(formSource, "private void SetSelectedWindowHandle(IntPtr handle)");
            string protocolMethod = GetMethodSlice(protocolSource, "void IProtocolCommandHost.HandleYikeContext(YikeWindowContext context)");

            Assert.Contains("CurrentSyncType != TYPE_YIKE", resolveMethod);
            Assert.Contains("YikeWindowContext.CopyOf(lastYikeWindowContext)", resolveMethod);
            Assert.DoesNotContain("ClearYikeContext()", resolveMethod);

            Assert.Contains("ClearYikeContext();", setHandleMethod);

            Assert.Contains("CurrentSyncType != TYPE_YIKE", protocolMethod);
            Assert.Contains("lastYikeWindowContext = YikeWindowContext.CopyOf(context);", protocolMethod);
            Assert.Contains("sessionCoordinator.SetYikeContext(lastYikeWindowContext);", protocolMethod);
        }

        [Fact]
        public void MainForm_SyncLifecycleDoesNotClearYikeContext()
        {
            string formSource = LoadSource("readboard", "Form1.cs");
            string keepStartedMethod = GetMethodSlice(formSource, "private void ApplyKeepSyncStartedUi()");
            string keepStoppedMethod = GetMethodSlice(formSource, "private void ApplyKeepSyncStoppedUi(bool continuousSyncActive)");
            string continuousStartedMethod = GetMethodSlice(formSource, "private void ApplyContinuousSyncStartedUi()");
            string resetMethod = GetMethodSlice(formSource, "void ISyncCoordinatorHost.OnSyncCachesReset()");

            Assert.DoesNotContain("ClearYikeContext()", keepStartedMethod);
            Assert.DoesNotContain("ClearYikeContext()", keepStoppedMethod);
            Assert.DoesNotContain("ClearYikeContext()", continuousStartedMethod);
            Assert.DoesNotContain("ClearYikeContext()", resetMethod);
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

        private static string GetMethodSlice(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing signature: {signature}");
            int braceStart = source.IndexOf('{', start);
            int depth = 0;
            for (int index = braceStart; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}')
                    depth--;

                if (depth == 0)
                    return source.Substring(start, index - start + 1);
            }

            throw new InvalidOperationException($"Could not slice method: {signature}");
        }
    }
}
