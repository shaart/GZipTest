using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.IO;                        // File, FileInfo, FileStream
using System.Threading;                 // Thread

namespace GZipTest
{
    /// <summary>
    /// Writing thread with queue (FIFO)
    /// </summary>
    class DebugWriterQueue : IDisposable
    {
        /// <summary>
        /// Signal for queue
        /// </summary>
        EventWaitHandle wh = new AutoResetEvent(false);
        /// <summary>
        /// Thread for writing to result file
        /// </summary>
        Thread worker;
        public Thread WorkerThread { get { return worker; } }
        /// <summary>
        /// Locker for queue
        /// </summary>
        object locker = new object();
        /// <summary>
        /// Locker for console
        /// </summary>
        object consoleLocker;
        Queue<Byte[]> tasks = new Queue<byte[]>();
        /// <summary>
        /// Number of read threads
        /// </summary>
        int readThreadCount = 0;
        /// <summary>
        /// Number of processed tasks
        /// </summary>
        int numberOfProcessedTasks = 0;
        /// <summary>
        /// "Local id" (number in my thread pool) of thread which must writing in result file
        /// </summary>
        int numberOfWaitingThread = 0;
        public int NumberOfWaitingThread { get { return numberOfWaitingThread; } }
        /// <summary>
        /// How many bytes already writed to file (current result file size)
        /// </summary>
        private long bytesWrited = 0;
        /// <summary>
        /// Property. Contains how many bytes already writed to file (current result file size)
        /// </summary>
        public long BytesWrited { get { return bytesWrited; } }

        #region DEBUG VARIABLES
        // For debug
        private int tasksCount = 0;
        public long TasksCount { get { return tasksCount; } }
        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="resultFilePath">Full path to result file</param>
        /// <param name="readThreadCount">Number of read threads</param>
        /// <param name="consoleCursorTop">Console cursor row for writing info of this thread</param>
        /// <param name="showMessagesInConsole">Should the output messages to the console</param>
        /// <param name="consoleLock">object for console lock</param>
        public DebugWriterQueue(string resultFilePath, int readThreadCount, int consoleCursorTop, bool showMessagesInConsole, object consoleLock)
        {
            this.readThreadCount = readThreadCount;
            this.consoleLocker = consoleLock;
            WriteParameters wPar = new WriteParameters(resultFilePath, consoleCursorTop, showMessagesInConsole);
            worker = new Thread(Work);
            worker.Name = "Write Thread";
            worker.IsBackground = true;
            worker.Start(wPar);
        }

        /// <summary>
        /// Enqueues new task to tasks
        /// </summary>
        /// <param name="task">Data which must be written to result file</param>
        /// <param name="threadNumber">"Local" id of reading thread</param>
        /// <returns>Is task enqueued</returns>
        public bool EnqueueTask(byte[] task, int threadNumber)
        {
            if (threadNumber == numberOfWaitingThread)
            {
                lock (locker)
                {
                    tasks.Enqueue(task);
                }
                wh.Set();                   // Signal what queue get new task
                numberOfWaitingThread++;
                if (numberOfWaitingThread == readThreadCount)
                {
                    numberOfWaitingThread = 0;
                }
                tasksCount = tasks.Count;   // For debug
                return true;                // Task enqueued
            }
            else
            {
                return false;               // Task not enqueued
            }
        }

        /// <summary>
        /// Task enqueue for Dispose method
        /// </summary>
        /// <param name="task"></param>
        private void EnqueueTask(byte[] task)
        {
            lock (locker)
            {
                tasks.Enqueue(task);
            }
            wh.Set();                   // Signal what queue get new task
            tasksCount = tasks.Count;   // For debug
        }

        /// <summary>
        /// Signal to finish working, sends null task to thread and waits for it's ending
        /// </summary>
        public void Dispose()
        {
            EnqueueTask(null);      // Signal for Writer to finishing
            worker.Join();          // Wait for ending Writer
            wh.Close();             // Free handle resources
        }

        /// <summary>
        /// Main loop (thread)
        /// </summary>
        /// <param name="objParameters">Struct "WriteParameters"</param>
        void Work(object objParameters)
        {
            WriteParameters parameters;
            if (objParameters != null)
            {
                parameters = (WriteParameters)objParameters;
            }
            else
            {
                throw new Exception("Can't start compress writer-thread without parameters");
            }
            try
            {
                using (FileStream outFile = File.Create(parameters.resultFilePath))
                {
                    while (true)
                    {
                        byte[] task = null;

                        lock (locker)
                        {
                            if (tasks.Count > 0)
                            {
                                task = tasks.Dequeue();
                                tasksCount = tasks.Count;
                                if (task == null)
                                {
                                    return;
                                }
                            }
                        }
                        if (task != null)
                        {
                            outFile.Write(task, 0, task.Length);                        // Write compressed bytes to file
                            bytesWrited += task.Length;
                            numberOfProcessedTasks++;
                            if (parameters.showMessagesInConsole)
                            {
                                lock (consoleLocker)
                                {
                                    System.Console.CursorTop = parameters.consoleCursorTop;
                                    System.Console.CursorLeft = 0;
                                    //System.Console.Write("Processed tasks: {0}. Part of current task: {1} => Writed to file.\n", numberOfProcessedTasks, task[0].ToString());
                                    System.Console.Write("File: {0}. Processed tasks: {1}\n", outFile.Name, numberOfProcessedTasks);
                                    //System.Console.Write("String writed to file: " + Encoding.Default.GetString(task));
                                    /*
                                    for (int i = 0; i < task.Length; i++)
                                    {
                                        Encoding.Default.GetString(task);
                                        System.Console.Write(task[i].ToString() + " ");
                                    }
                                    */
                                    //System.Console.Write("=> Writed to file.\n");
                                }
                            }
                        }
                        else                // No tasks
                        {
                            wh.WaitOne();   // Wait for a signal
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
