using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace readboard
{
    internal partial class FoxAutoPlayIdentityDialog : Form
    {
        private const int CandidatePanelHeight = 52;
        private const int CandidatePanelGap = 6;
        private const int CandidatePreviewLeft = 86;
        private readonly List<FoxAutoPlayIdentityCandidate> detectedCandidates = new List<FoxAutoPlayIdentityCandidate>();
        private readonly List<RadioButton> candidateRadioButtons = new List<RadioButton>();

        internal FoxAutoPlayIdentityDialog(string currentNickname, string currentNicknameSignature)
            : this(currentNicknameSignature, (IEnumerable<FoxAutoPlayIdentityCandidate>)null)
        {
        }

        internal FoxAutoPlayIdentityDialog(
            string currentNickname,
            string currentNicknameSignature,
            IEnumerable<FoxAutoPlayIdentityCandidate> candidates)
            : this(currentNicknameSignature, candidates)
        {
        }

        internal FoxAutoPlayIdentityDialog(
            string currentNicknameSignature,
            IEnumerable<FoxAutoPlayIdentityCandidate> candidates)
        {
            InitializeComponent();
            ApplyLanguage();
            SelectedNickname = string.Empty;
            SelectedNicknameSignature = currentNicknameSignature ?? string.Empty;
            LoadCandidates(candidates);
        }

        internal string SelectedNickname { get; private set; }
        internal string SelectedNicknameSignature { get; private set; }

        private void ApplyLanguage()
        {
            Text = getLangStr("FoxAutoPlayIdentityDialog_title");
            lblPrompt.Text = getLangStr("FoxAutoPlayIdentityDialog_lblPrompt");
            lblDetectedNicknames.Text = getLangStr("FoxAutoPlayIdentityDialog_lblDetectedNicknames");
            btnConfirm.Text = getLangStr("FoxAutoPlayIdentityDialog_btnConfirm");
            btnCancel.Text = getLangStr("FoxAutoPlayIdentityDialog_btnCancel");
        }

        private string getLangStr(string itemName)
        {
            try
            {
                return Program.CurrentContext.LanguageItems[itemName].ToString();
            }
            catch (Exception)
            {
                return itemName;
            }
        }

        private void LoadCandidates(IEnumerable<FoxAutoPlayIdentityCandidate> candidates)
        {
            ClearCandidateControls();
            detectedCandidates.Clear();
            if (candidates != null)
            {
                foreach (FoxAutoPlayIdentityCandidate candidate in candidates)
                {
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.NicknameSignature))
                        continue;
                    detectedCandidates.Add(candidate);
                    AddCandidateControl(detectedCandidates.Count - 1, candidate);
                }
            }

            if (detectedCandidates.Count == 0)
            {
                AddNoCandidateLabel();
                pnlDetectedPlayerRows.Enabled = false;
                return;
            }

            pnlDetectedPlayerRows.Enabled = true;
        }

        private void AddCandidateControl(int index, FoxAutoPlayIdentityCandidate candidate)
        {
            Panel candidatePanel = new Panel
            {
                Left = 0,
                Top = index * (ScaleValue(CandidatePanelHeight) + ScaleValue(CandidatePanelGap)),
                Width = Math.Max(ScaleValue(420), pnlDetectedPlayerRows.ClientSize.Width - ScaleValue(22)),
                Height = ScaleValue(CandidatePanelHeight),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };

            RadioButton radioButton = new RadioButton
            {
                AutoSize = true,
                Left = ScaleValue(8),
                Top = ScaleValue(16),
                Text = candidate.Nickname
            };
            radioButton.CheckedChanged += delegate
            {
                if (radioButton.Checked)
                    SelectCandidate(index);
            };
            candidateRadioButtons.Add(radioButton);
            candidatePanel.Controls.Add(radioButton);

            PictureBox preview = new PictureBox
            {
                Left = ScaleValue(CandidatePreviewLeft),
                Top = ScaleValue(4),
                Width = Math.Max(ScaleValue(260), candidatePanel.Width - ScaleValue(CandidatePreviewLeft + 10)),
                Height = ScaleValue(42),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = candidate.PreviewImage
            };
            preview.Click += delegate { radioButton.Checked = true; };
            candidatePanel.Click += delegate { radioButton.Checked = true; };
            candidatePanel.Controls.Add(preview);
            pnlDetectedPlayerRows.Controls.Add(candidatePanel);
        }

        private void AddNoCandidateLabel()
        {
            Label label = new Label
            {
                AutoSize = false,
                Left = ScaleValue(10),
                Top = ScaleValue(10),
                Width = Math.Max(ScaleValue(360), pnlDetectedPlayerRows.ClientSize.Width - ScaleValue(20)),
                Height = ScaleValue(24),
                Text = getLangStr("FoxAutoPlayIdentityDialog_noDetectedNicknames")
            };
            pnlDetectedPlayerRows.Controls.Add(label);
        }

        private void SelectCandidate(int selectedIndex)
        {
            for (int i = 0; i < candidateRadioButtons.Count; i++)
            {
                if (i != selectedIndex)
                    candidateRadioButtons[i].Checked = false;
            }
        }

        private int GetSelectedCandidateIndex()
        {
            for (int i = 0; i < candidateRadioButtons.Count; i++)
            {
                if (candidateRadioButtons[i].Checked)
                    return i;
            }
            return -1;
        }

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            int selectedIndex = GetSelectedCandidateIndex();
            if (selectedIndex < 0 || selectedIndex >= detectedCandidates.Count)
            {
                MessageBox.Show(this, getLangStr("FoxAutoPlayIdentityDialog_noSelectedPlayerRow"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectedNickname = string.Empty;
            SelectedNicknameSignature = ResolveSelectedSignature(selectedIndex);
            DialogResult = DialogResult.OK;
            Close();
        }

        private string ResolveSelectedSignature(int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= detectedCandidates.Count)
                return string.Empty;

            FoxAutoPlayIdentityCandidate candidate = detectedCandidates[selectedIndex];
            return candidate.NicknameSignature ?? string.Empty;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private int ScaleValue(int value)
        {
            return (int)Math.Round(value * AutoScaleDimensions.Width / 6f);
        }

        private void ClearCandidateControls()
        {
            for (int i = pnlDetectedPlayerRows.Controls.Count - 1; i >= 0; i--)
                pnlDetectedPlayerRows.Controls[i].Dispose();
            pnlDetectedPlayerRows.Controls.Clear();
            candidateRadioButtons.Clear();
        }

        private void DisposeCandidates()
        {
            foreach (FoxAutoPlayIdentityCandidate candidate in detectedCandidates)
            {
                if (candidate != null)
                    candidate.Dispose();
            }
            detectedCandidates.Clear();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClearCandidateControls();
            DisposeCandidates();
            base.OnFormClosed(e);
        }
    }

    internal sealed class FoxAutoPlayIdentityCandidate : IDisposable
    {
        public FoxAutoPlayIdentityCandidate(string nickname, string nicknameSignature)
            : this(nickname, nicknameSignature, null)
        {
        }

        public FoxAutoPlayIdentityCandidate(string nickname, string nicknameSignature, Bitmap previewImage)
        {
            Nickname = nickname ?? string.Empty;
            NicknameSignature = nicknameSignature ?? string.Empty;
            PreviewImage = previewImage;
        }

        public string Nickname { get; private set; }
        public string NicknameSignature { get; private set; }
        public Bitmap PreviewImage { get; private set; }

        public void Dispose()
        {
            if (PreviewImage != null)
            {
                PreviewImage.Dispose();
                PreviewImage = null;
            }
        }
    }
}
