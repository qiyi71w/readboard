using System;
using System.Drawing;
using System.Text.Json;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class IdentityBridgeTests
    {
        [Theory]
        [InlineData("identity.open", "{}", true)]
        [InlineData("identity.close", "{}", true)]
        [InlineData("identity.clearSaved", "{}", true)]
        [InlineData("identity.select", "{\"candidateId\":\"candidate-1\"}", true)]
        [InlineData("identity.useOnce", "{\"candidateId\":\"candidate-1\"}", true)]
        [InlineData("identity.saveAndUse", "{\"candidateId\":\"candidate-1\"}", true)]
        [InlineData("identity.open", "{\"extra\":true}", false)]
        [InlineData("identity.select", "{\"candidateId\":\"\"}", false)]
        [InlineData("identity.select", "{\"candidateId\":\"candidate-1\",\"extra\":true}", false)]
        [InlineData("identity.unknown", "{}", false)]
        public void IdentityCommands_UseStrictPayloads(string type, string payloadJson, bool expected)
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            ReadBoardUiCommand command = new ReadBoardUiCommand
            {
                Type = type,
                Payload = document.RootElement.Clone()
            };

            Assert.Equal(expected, MainForm.IsValidWebViewIdentityCommand(command));
        }

        [Fact]
        public void IdentityCandidateState_DoesNotExposeNicknameSignature()
        {
            ReadBoardIdentityCandidateUiState candidate = new ReadBoardIdentityCandidateUiState
            {
                Id = "candidate-1",
                Label = "玩家行 1",
                PreviewUrl = "data:image/png;base64,AA=="
            };

            string json = JsonSerializer.Serialize(candidate);

            Assert.DoesNotContain("signature", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("candidate-1", json);
        }

        [Fact]
        public void EncodeIdentityPreview_ProducesPngDataUrl()
        {
            using Bitmap bitmap = new Bitmap(2, 2);

            string value = MainForm.EncodeIdentityPreview(bitmap);

            Assert.StartsWith("data:image/png;base64,", value, StringComparison.Ordinal);
            Assert.NotEmpty(Convert.FromBase64String(value.Substring("data:image/png;base64,".Length)));
        }

        [Fact]
        public void EncodeIdentityPreview_AllowsMissingPreview()
        {
            Assert.Null(MainForm.EncodeIdentityPreview(null));
        }
    }
}
