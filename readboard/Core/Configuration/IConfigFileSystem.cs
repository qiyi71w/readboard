using System.IO;
using System.Text;

namespace readboard
{
    internal interface IConfigFileSystem
    {
        void CreateDirectory(string path);
        bool DirectoryExists(string path);
        void WriteAllText(string path, string content);
        bool FileExists(string path);
        void Copy(string sourcePath, string destinationPath);
        void ReplaceOrMove(string sourcePath, string destinationPath);
        void DeleteFile(string path);
        void DeleteDirectory(string path);
    }

    internal sealed class PhysicalConfigFileSystem : IConfigFileSystem
    {
        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public void WriteAllText(string path, string content)
        {
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public void Copy(string sourcePath, string destinationPath)
        {
            File.Copy(sourcePath, destinationPath);
        }

        public void ReplaceOrMove(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(sourcePath, destinationPath, null);
                return;
            }

            File.Move(sourcePath, destinationPath);
        }

        public void DeleteFile(string path)
        {
            File.Delete(path);
        }

        public void DeleteDirectory(string path)
        {
            Directory.Delete(path, true);
        }
    }
}
