using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GZipTest
{
    /// <summary>
    /// Выполняет асинхронное чтение из одного стрима с асинхронной записью в другой стрим
    /// </summary>
    public abstract class BufferedStreamMove
    {
        static bool DEBUG = false; // Режим вывода дополнительных логов в консоль
        static bool OUT_PERCENT = false; // Режим вывода прогресса в консоль

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

        private long _inputLength; // Размер исходного файла
        private int _progress = 0; // Прогресс записи в % в диапазоне 0..100
        private bool _isStart = false; // Признак запуска процесса
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
        /// Прогресс записи выходного стрима
        /// </summary>
        public int Progress => _progress;

        /// <summary>
        /// Возвращает true при успешном окончании
        /// </summary>
        public bool Success => _success;

        /// <summary>
        /// Запускает процесс перемещения стримов. Возвращает текущий экземпляр
        /// </summary>
        /// <returns>Текущий экземпляр</returns>
        public BufferedStreamMove Start()
        {
            if (_isStart) throw new InvalidOperationException("Process already started"); // TODO Можно сделать отдельное исключение на то что процесс был запущен и уже остоновился

            _isStart = true;
            _readThread.Start();
            _writeThread.Start();
            return this;
        }

        /// <summary>
        /// Создает входной поток
        /// </summary>
        /// <returns></returns>
        protected abstract Stream CreateInputStream();

        /// <summary>
        /// Создает выходной поток
        /// </summary>
        /// <returns></returns>
        protected abstract Stream CreateOutputStream();

        private void ReadFile() // Поток чтения входного стрима
        {
            try
            {
                using (var inStream = CreateInputStream()) // Создаем входной стрим
                {
                    if (inStream.CanSeek) // Длина стрима может быть не доступна
                        _inputLength = inStream.Length;

                    int count; // Переменная для числа считанных байт
                    while (!_exitThreadEvent.WaitOne(0, false)) // Цикл пока процес не остановят
                    {
                        byte[] data = new byte[BLOCK_SIZE]; // Считываем данные из потока
                        count = inStream.Read(data, 0, data.Length);
                        if (count == 0) break;

                        if (DEBUG) Console.WriteLine("Read {0}", count);
                        if (data.Length != count) // Размер считанного блока не совпадает. Изменяем размер массива
                            Array.Resize(ref data, count);

                        lock (_syncRoot) // Исключаем параллельное использование очереди
                        {
                            _queue.Enqueue(data); // Добавляем в очередь
                            _newItemEvent.Set(); // Сигналим о добавлении нового элемента
                        }

                        if (_queue.Count >= MAX_BLOCKS) // Превысили верхний порог
                            while (_queue.Count > MIN_BLOCKS) // Ждем пока опустимся до нижнего порога
                            {
                                if (WaitHandle.WaitAny(_readEventArray) == 1) // Возможна ситуация, когда процесс остановили - WaitAny вернет 1
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
                _success = false; // Отмечаем, что процесс завершился с ошибкой
                Stop(); // Останавливаем второй поток
            }
        }

        private void WriteFile() // Поток записи в выходной стрим
        {
            try
            {
                using (var outStream = CreateOutputStream()) // Создаем выходной стрим
                {
                    long totalWrite = 0; // Счетчик записанных байт
                    while (!_exitThreadEvent.WaitOne(0, false)) // Цикл пока процес не остановят
                    {
                        // Пытаемся получить блок данных из очереди
                        byte[] data = null;
                        lock (_syncRoot)   // Исключаем параллельное использование
                        {
                            if (_queue.Count > 0) // Очередь не пуста
                            {
                                data = _queue.Dequeue(); // Извлекаем блок
                                _dequeueItemEvent.Set(); // Сообщаем, что мы извлекли блок из очереди
                            }
                        }

                        if (data == null) // Очередь пуста
                        {
                            if (_readCompleted) break; // Чтение закончено, выходим

                            if (WaitHandle.WaitAny(_writeEventArray) == 1) // Ждем добавления нового блока или прерывания
                                break;
                        }
                        else // Успешно извлекли блок
                        {
                            if (DEBUG) Console.WriteLine("Write {0}", data.Length);
                            outStream.Write(data, 0, data.Length);

                            // Считаем прогресс
                            totalWrite += data.Length;
                            if (_inputLength != 0) // Длина может быть равна 0, если поток чтения не успел открыть файл, или входной поток не поддерживает определение длины
                            {
                                int p = (int)(100 * totalWrite / _inputLength);
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
                _success = false; // Отмечаем, что процесс завершился с ошибкой
                Stop(); // Останавливаем второй поток
                OnWriteInterrupted(); // Не забываем удалить файлы и пр.
            }
        }

        /// <summary>
        /// Вызывается при прерывании потока записи. Позволяет удалить файл назначения при прерывании или ошибке
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
