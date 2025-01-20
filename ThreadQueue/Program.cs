using System;
using System.Collections.Generic;
using System.Threading;

public class SendAdsEvent
{
    public Thread _thread { get; set; }
    public int _timeout { get; set; }
    public int _nRetValue { get; set; }
}

public class ThreadQueue
{
    private Queue<SendAdsEvent> _taskQueue = new Queue<SendAdsEvent>();
    private readonly object _lockObject = new object();
    private bool _stopRequested = false;

    public void Enqueue(SendAdsEvent task)
    {
        lock (_lockObject)
        {
            _taskQueue.Enqueue(task);
            Monitor.Pulse(_lockObject); // 通知工作线程有新任务
        }
    }

    public void Start()
    {
        Thread workerThread = new Thread(DoWork);
        workerThread.Start();
    }

    private void DoWork()
    {
        while (!_stopRequested)
        {
            SendAdsEvent taskToExecute = null;

            lock (_lockObject)
            {
                while (_taskQueue.Count == 0 && !_stopRequested)
                {
                    Monitor.Wait(_lockObject); // 等待新任务或停止请求
                }

                if (_taskQueue.Count > 0)
                {
                    taskToExecute = _taskQueue.Dequeue();
                }
            }

            if (taskToExecute != null)
            {
                taskToExecute._thread.Start();
                if (!taskToExecute._thread.Join(taskToExecute._timeout))
                {
                    taskToExecute._nRetValue = -1;
                    taskToExecute._thread.Abort();
                }
                else
                {
                    taskToExecute._nRetValue = 0; // 设置成功的返回值
                }
            }
        }
    }

    public void Stop()
    {
        _stopRequested = true;
        lock (_lockObject)
        {
            Monitor.Pulse(_lockObject); // 唤醒工作线程以处理停止请求
        }
    }
}

class Program
{
    static void Main(string[] args)
    {
        ThreadQueue queue = new ThreadQueue();
        queue.Start();

        // 将任务添加到队列
        for (int i = 0; i < 5; i++)
        {
            SendAdsEvent task = new SendAdsEvent
            {
                _thread = new Thread(() => Console.WriteLine("任务执行: " + DateTime.Now)),
                _timeout = 3000
            };
            queue.Enqueue(task);
        }

        // 等待一段时间，让任务执行
        Thread.Sleep(9000);

        // 停止队列
        queue.Stop();

        Console.WriteLine("所有任务执行完毕。");
    }
}