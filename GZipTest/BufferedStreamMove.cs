using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GZipTest
{
    /// <summary>
    /// Выполняет асинхронное чтение из одного потока с асинхронной записью в другой поток
    /// </summary>
    public abstract class BufferedStreamMove
    {
        static bool DEBUG = false; // Режим вывода дополнительных логов в консоль
        static bool OUT_PERCENT = true; // Режим вывода прогресса в консоль

        const int BLOCK_SIZE = 10 * 1024; // Размер одного блока (10 Kb)
        const int MAX_BLOCKS = 30; // При заполнении очереди до MAX_BLOCKS ...
        const int MIN_BLOCKS = 1;  // ... поток чтения ждет пока она опустошится до MIN_BLOCKS

        private Queue<byte[]> _queue; // Очередь блоков
        private object _syncRoot; // Объект синхронизации очереди

        private bool _readCompleted; // Признак завершения чтения

        private EventWaitHandle _newItemEvent; // Событие добавления элемента в очередь
        private EventWaitHandle _dequeueItemEvent; // Событие извлечения из очереди
        private EventWaitHandle _exitThreadEvent; // События прерывания процесса
        private WaitHandle[] _readEventArray; // События для ожидания освобождения очереди при чтении

        private WaitHandle[] _writeEventArray; // События для ожидания нового элемента при записи

        private Thread _readThread, _writeThread; // Потоки чтения и записи

        private long _fileSize; // Размер исходного файла
        private int _progress = 0; // Прогресс записи в % в диапазоне 0..100
        private bool _success = true; // Признак успешного выполнения. Если потоки прерываются из-за ошибки, устанавливается в false

        public BufferedStreamMove()
        {
            _newItemEvent = new AutoResetEvent(false);
            _dequeueItemEvent = new AutoResetEvent(false);
            _exitThreadEvent = new ManualResetEvent(false);
            _readEventArray = new WaitHandle[] { _dequeueItemEvent, _exitThreadEvent };
            _writeEventArray = new WaitHandle[] { _newItemEvent, _exitThreadEvent };

            _queue = new Queue<byte[]>();
            _syncRoot = ((ICollection)_queue).SyncRoot;

            _readThread = new Thread(ReadFile);
            _writeThread = new Thread(WriteFile);
        }

        /// <summary>
        /// Прогресс обработки потока
        /// </summary>
        public int Progress => _progress;

        /// <summary>
        /// Возвращает true при успешном окончании
        /// </summary>
        public bool Success => _success;

        /// <summary>
        /// Запускает процесс перемещения потоков. Возвращает текущий экземпляр
        /// </summary>
        public BufferedStreamMove Start()
        {
            _readThread.Start();
            _writeThread.Start();
            return this;
        }

        protected abstract Stream CreateOutputStream();

        protected abstract Stream CreateInputStream();

        private void ReadFile()
        {
            try
            {
                byte[] buff = new byte[BLOCK_SIZE];
                int count;

                using (var inStream = CreateInputStream())
                {
                    if (inStream.CanSeek)
                        _fileSize = inStream.Length;
                    while (!_exitThreadEvent.WaitOne(0, false))
                    {
                        count = inStream.Read(buff, 0, buff.Length);
                        if (count == 0) break;

                        lock (_syncRoot)
                        {
                            if (DEBUG) Console.WriteLine("Read {0}", count);
                            byte[] data = new byte[count];
                            Array.Copy(buff, data, count);
                            _queue.Enqueue(data);
                            _newItemEvent.Set();
                        }

                        if (_queue.Count >= MAX_BLOCKS)
                            while (_queue.Count > MIN_BLOCKS)
                            {
                                int w = WaitHandle.WaitAny(_readEventArray);
                                if (w == 1)
                                    break;
                            }
                    }
                }

                if (DEBUG) Console.WriteLine("Read completed");
                _readCompleted = true;
            }
            catch (Exception ex)
            {
                if (DEBUG) Console.WriteLine(ex);
                _success = false;
                Stop();
            }
        }

        private void WriteFile()
        {
            try
            {
                using (var outStream = CreateOutputStream())
                {
                    long totalWrite = 0; // Счетчик записанных байт
                    while (!_exitThreadEvent.WaitOne(0, false)) // Цикл пока процес не остановят
                    {
                        byte[] buff = null;
                        lock (_syncRoot)
                        {
                            if (_queue.Count > 0)
                            {
                                buff = _queue.Dequeue();
                                _dequeueItemEvent.Set(); // Сообщаем, что мы извлекли блок из очереди
                            }
                        }

                        if (buff == null) // Очередь пуста
                        {
                            if (_readCompleted) break; // Чтение закончено, выходим

                            if (WaitHandle.WaitAny(_writeEventArray) == 1) // Ждем добавления нового или прерывания
                                break;
                        }
                        else
                        {
                            if (DEBUG) Console.WriteLine("Write {0}", buff.Length);
                            outStream.Write(buff, 0, buff.Length);

                            // Считаем прогресс
                            totalWrite += buff.Length;
                            if (_fileSize != 0) // _fileSize может быть равен 0, если поток чтения не успел открыть файл или входной поток не поддерживает определение длины
                            {
                                int p = (int)(100 * totalWrite / _fileSize);
                                if (p != _progress) // Выводим прогресс, только если он поменялся
                                {
                                    _progress = p;
                                    Console.WriteLine(_progress + "%");
                                }
                            }
                        }
                    }
                }

                if (_exitThreadEvent.WaitOne(0, false))
                    OnWriteInterrupted();
                else
                   if (DEBUG) Console.WriteLine("Write completed");
            }
            catch (Exception ex)
            {
                if (DEBUG) Console.WriteLine(ex);
                _success = false;
                Stop();
            }
        }

        /// <summary>
        /// Вызывается при прерывании потока записи
        /// </summary>
        protected virtual void OnWriteInterrupted()
        {
        }

        /// <summary>
        /// Останавливает процесс перемещения
        /// </summary>
        public void Stop()
        {
            _exitThreadEvent.Set();
        }

        /// <summary>
        /// Ожидает остановки процесса перемещения
        /// </summary>
        public bool Wait()
        {
            _readThread.Join();
            _writeThread.Join();
            return _success;
        }
    }
}
