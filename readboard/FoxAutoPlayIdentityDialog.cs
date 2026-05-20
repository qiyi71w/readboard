using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace readboard
{
    internal partial class FoxAutoPlayIdentityDialog : Form
    {
        private readonly List<FoxAutoPlayIdentityCandidate> detectedCandidates = new List<FoxAutoPlayIdentityCandidate>();

        internal FoxAutoPlayIdentityDialog(string currentNickname, string currentNicknameSignature)
            : this(currentNickname, currentNicknameSignature, null)
        {
        }

        internal FoxAutoPlayIdentityDialog(
            string currentNickname,
            string currentNicknameSignature,
            IEnumerable<FoxAutoPlayIdentityCandidate> candidates)
        {
            InitializeComponent();
            ApplyLanguage();
            chkRememberNickname.Checked = true;
            txtNickname.Text = currentNickname ?? string.Empty;
            SelectedNickname = txtNickname.Text.Trim();
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
            lblManualNickname.Text = getLangStr("FoxAutoPlayIdentityDialog_lblManualNickname");
            chkRememberNickname.Text = getLangStr("FoxAutoPlayIdentityDialog_chkRememberNickname");
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
            detectedCandidates.Clear();
            lstDetectedNicknames.Items.Clear();
            if (candidates != null)
            {
                foreach (FoxAutoPlayIdentityCandidate candidate in candidates)
                {
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.Nickname))
                        continue;
                    detectedCandidates.Add(candidate);
                    lstDetectedNicknames.Items.Add(candidate.Nickname);
                }
            }

            if (detectedCandidates.Count == 0)
            {
                lstDetectedNicknames.Items.Add(getLangStr("FoxAutoPlayIdentityDialog_noDetectedNicknames"));
                lstDetectedNicknames.Enabled = false;
                return;
            }

            lstDetectedNicknames.Enabled = true;
            for (int i = 0; i < detectedCandidates.Count; i++)
            {
                if (string.Equals(detectedCandidates[i].Nickname, txtNickname.Text.Trim(), StringComparison.Ordinal))
                {
                    lstDetectedNicknames.SelectedIndex = i;
                    return;
                }
            }
        }

        private void lstDetectedNicknames_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstDetectedNicknames.SelectedIndex < 0 || lstDetectedNicknames.SelectedIndex >= detectedCandidates.Count)
                return;

            txtNickname.Text = detectedCandidates[lstDetectedNicknames.SelectedIndex].Nickname;
        }

        private void btnConfirm_Click(object sender, EventArgs e)
        {
            string nickname = txtNickname.Text.Trim();
            if (nickname.Length == 0)
            {
                MessageBox.Show(this, getLangStr("FoxAutoPlayIdentityDialog_emptyNickname"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectedNickname = nickname;
            SelectedNicknameSignature = chkRememberNickname.Checked ? ResolveSelectedSignature(nickname) : string.Empty;
            DialogResult = DialogResult.OK;
            Close();
        }

        private string ResolveSelectedSignature(string nickname)
        {
            if (!lstDetectedNicknames.Enabled
                || lstDetectedNicknames.SelectedIndex < 0
                || lstDetectedNicknames.SelectedIndex >= detectedCandidates.Count)
                return string.Empty;

            FoxAutoPlayIdentityCandidate candidate = detectedCandidates[lstDetectedNicknames.SelectedIndex];
            return candidate.NicknameSignature ?? string.Empty;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    internal sealed class FoxAutoPlayIdentityCandidate
    {
        public FoxAutoPlayIdentityCandidate(string nickname, string nicknameSignature)
        {
            Nickname = nickname ?? string.Empty;
            NicknameSignature = nicknameSignature ?? string.Empty;
        }

        public string Nickname { get; private set; }
        public string NicknameSignature { get; private set; }
    }
}
