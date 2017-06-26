using System.IO;
using System.IO.Compression;

namespace GZipTest
{
    /// <summary>
    /// Выполняет асинхронную запаковку в формат gzip
    /// </summary>
    public class GZipCompressor : BufferedStreamMove
    {
        private string _sourceFile, _destArchive; // Исходный файл и архив назначения

        /// <summary>
        /// Инициирует экземпляр запаковщика
        /// </summary>
        /// <param name="sourceFile">Путь к исходному файлу</param>
        /// <param name="destArchive">Путь к архиву назначения</param>
        public GZipCompressor(string sourceFile, string destArchive)
        {
            _sourceFile = sourceFile;
            _destArchive = destArchive;
        }

        protected override Stream CreateInputStream()
        {
            return File.OpenRead(_sourceFile);
        }

        protected override Stream CreateOutputStream()
        {
            return new GZipStream(File.Create(_destArchive), CompressionMode.Compress);
        }

        protected override void OnWriteInterrupted()
        {
            File.Delete(_destArchive);
        }
    }
}
