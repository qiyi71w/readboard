using System.Collections;
using System.Drawing;
using System.Threading;

namespace readboard
{
    internal sealed class RuntimeContext
    {
        private Bitmap boardBitmap;

        public RuntimeContext(LaunchOptions launchOptions, AppConfig config, SessionState session)
        {
            LaunchOptions = launchOptions;
            Config = config;
            Session = session;
            LanguageItems = new Hashtable();
        }

        public LaunchOptions LaunchOptions { get; private set; }
        public AppConfig Config { get; set; }
        public SessionState Session { get; private set; }
        public Hashtable LanguageItems { get; private set; }
        public string Language { get; set; }
        public bool HasConfigFile { get; set; }
        public bool IsScaled { get; set; }

        public Bitmap BoardBitmap
        {
            get { return boardBitmap; }
        }

        public void ReplaceBoardBitmap(Bitmap newBitmap)
        {
            Bitmap oldBitmap = Interlocked.Exchange(ref boardBitmap, newBitmap);
            if (oldBitmap != null)
                oldBitmap.Dispose();
        }

        public void DisposeBoardBitmap()
        {
            ReplaceBoardBitmap(null);
        }
    }
}
