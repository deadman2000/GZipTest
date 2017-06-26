using System.IO;
using System.IO.Compression;

namespace GZipTest
{
    /// <summary>
    /// Выполняет асинхронную распаковку из формата gzip
    /// </summary>
    public class GZipDecompressor : BufferedStreamMove
    {
        private string _sourceArchive, _destFile; // Исходный файл и архив назначения

        /// <summary>
        /// Инициирует экземпляр распаковщика
        /// </summary>
        /// <param name="sourceArchive">Путь к исходному архиву</param>
        /// <param name="destFile">Путь к файлу назначения</param>
        public GZipDecompressor(string sourceArchive, string destFile)
        {
            _sourceArchive = sourceArchive;
            _destFile = destFile;
        }

        protected override Stream CreateInputStream()
        {
            return new GZipStream(File.OpenRead(_sourceArchive), CompressionMode.Decompress);
        }

        protected override Stream CreateOutputStream()
        {
            return File.Create(_destFile);
        }

        protected override void OnWriteInterrupted()
        {
            File.Delete(_destFile);
        }
    }
}
