using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DnrspEngine.Tools
{
    public class TfIdfValueManager
    {
        private double currentValue = 0.755; // 初始值
        private readonly double upperLimit = 0.79; // 上限
        private readonly double lowerLimit = 0.5; // 下限
        private readonly TimeSpan checkInterval = TimeSpan.FromMinutes(10); // 检查间隔
        private DateTime lastTriggerTime = DateTime.Now; // 上次触发时间
        private CancellationTokenSource cancellationTokenSource;

        public double CurrentValue
        {
            get { return currentValue; }
        }
        public TfIdfValueManager()
        {
            // 启动后台任务以异步检查时间间隔
            cancellationTokenSource = new CancellationTokenSource();
            Task.Run(async () => await CheckIntervalAsync(cancellationTokenSource.Token));
        }

        // 触发事件
        public void Trigger()
        {
            lastTriggerTime = DateTime.Now;
            currentValue -= 0.001;
            currentValue = Math.Max(currentValue, lowerLimit); // 不低于下限
        }

        // 异步检查时间间隔
        private async Task CheckIntervalAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(checkInterval, cancellationToken); // 等待10分钟

                if ((DateTime.Now - lastTriggerTime) >= checkInterval)
                {
                    currentValue += 0.001;
                    currentValue = Math.Min(currentValue, upperLimit); // 不超过上限

                }
            }
        }

        // 停止后台任务（可选）
        public void Stop()
        {
            cancellationTokenSource.Cancel();
        }
    }
    public class TfIdfCalculator
    {
        private List<string[]> _documents;
        private Dictionary<string, int> _documentFrequency;
        private int _totalDocuments;
        private static TfIdfCalculator _instance;
        private TfIdfValueManager tfIdfValueManager;
        private const int MinDataLength = 5;

        public static TfIdfCalculator Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TfIdfCalculator();
                }
                return _instance;
            }
        }

        public TfIdfCalculator()
        {
            tfIdfValueManager = new TfIdfValueManager();
            _documents = new List<string[]>();
            _documentFrequency = new Dictionary<string, int>();
        }
        public double GetCurTfIdfValue()
        {
            return tfIdfValueManager.CurrentValue;
        }

        // 实现接口方法：添加文档
        public void AddDocument(string[] document)
        {
            tfIdfValueManager.Trigger();

            _documents.Add(document);
            _totalDocuments++;

            var uniqueTerms = new HashSet<string>(document);
            foreach (var term in uniqueTerms)
            {
                if (_documentFrequency.ContainsKey(term))
                {
                    _documentFrequency[term]++;
                }
                else
                {
                    _documentFrequency[term] = 1;
                }
            }
        }

        // 实现接口方法：优化文档频率
        public void OptimizeDocumentFrequency()
        {

            _documentFrequency.Clear();
            _documents.Clear();
            _totalDocuments = 0;
        }
        public void InitDocuments(List<string> documents)
        {
            _documents = documents.Select(doc => doc.Trim().Split(' ')).ToList();
            _documentFrequency = new Dictionary<string, int>();
            _totalDocuments = _documents.Count;

            foreach (var doc in _documents)
            {
                var uniqueTerms = new HashSet<string>(doc);
                foreach (var term in uniqueTerms)
                {
                    if (_documentFrequency.ContainsKey(term))
                    {
                        _documentFrequency[term]++;
                    }
                    else
                    {
                        _documentFrequency[term] = 1;
                    }
                }
            }
        }

        // 计算词频（TF）
        public double TermFrequency(string[] doc, string term)
        {
            return doc.Count(t => t == term) / (double)doc.Length;
        }

        // 计算逆文档频率（IDF），使用平滑公式
        public double InverseDocumentFrequency(string term)
        {
            if (!_documentFrequency.ContainsKey(term))
            {
                return 0;
            }
            return Math.Log((1 + _totalDocuments) / (1 + (double)_documentFrequency[term])) + 1;
        }

        // 计算 TF-IDF 值
        public double TfIdf(string[] doc, string term)
        {
            return TermFrequency(doc, term) * InverseDocumentFrequency(term);
        }

        // 计算文档的 TF-IDF 向量
        public Dictionary<string, double> GetTfIdfVector(string[] doc)
        {
            var vector = new Dictionary<string, double>();
            var uniqueTerms = new HashSet<string>(doc);
            foreach (var term in uniqueTerms)
            {
                double dIdf = TfIdf(doc, term);
                vector[term] = dIdf;
            }
            return vector;
        }

        private double CalculateLengthPenalty(double length1, double length2)
        {
            double ratio = (Math.Min(length1, length2) / Math.Max(length1, length2));
            return ratio;
        }


        private Dictionary<string, double> SmoothTfIdfVector(Dictionary<string, double> vector, double smoothingFactor = 0.01)
        {
            var smoothedVector = new Dictionary<string, double>();
            foreach (var kvp in vector)
            {
                smoothedVector[kvp.Key] = kvp.Value + smoothingFactor;
            }
            return smoothedVector;
        }

        // 计算余弦相似度
        public double CosineSimilarity(Dictionary<string, double> vector1, Dictionary<string, double> vector2)
        {
            double dotProduct = 0;
            double normA = 0;
            double normB = 0;

            var allTerms = vector1.Keys.Union(vector2.Keys);
            foreach (var term in allTerms)
            {
                double a = vector1.ContainsKey(term) ? vector1[term] : 0;
                double b = vector2.ContainsKey(term) ? vector2[term] : 0;
                dotProduct += a * b;
                normA += Math.Pow(a, 2);
                normB += Math.Pow(b, 2);
            }

            normA = Math.Sqrt(normA);
            normB = Math.Sqrt(normB);

            if (normA == 0 || normB == 0)
            {
                return 0;
            }

            double similarity = dotProduct / (normA * normB);
            double lengthPenalty = CalculateLengthPenalty(vector1.Count, vector2.Count);

            return similarity * lengthPenalty;
        }

        // 计算新文档与已有文档的最大相似度
        public double CalculateMaxSimilarity(string[] newDoc)
        {
            try
            {
                if (_documents != null)
                {
                    var newVector = GetTfIdfVector(newDoc);
                    double maxSimilarity = 0;

                    foreach (var doc in _documents)
                    {
                        var vector = GetTfIdfVector(doc);
                        double similarity = CosineSimilarity(newVector, vector);
                        if (similarity > maxSimilarity)
                        {
                            maxSimilarity = similarity;
                        }
                    }

                    return maxSimilarity;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CalculateMaxSimilarity] Error:{ex.ToString()}");

                return 0;
            }
        }

        // 计算两个文档的相似度
        public double CalculateSimilarity(int docIndex1, int docIndex2)
        {
            if (docIndex1 < 0 || docIndex1 >= _totalDocuments || docIndex2 < 0 || docIndex2 >= _totalDocuments)
            {
                throw new IndexOutOfRangeException("Document index out of range.");
            }

            var vector1 = GetTfIdfVector(_documents[docIndex1]);
            var vector2 = GetTfIdfVector(_documents[docIndex2]);

            return CosineSimilarity(vector1, vector2);
        }
    }

    public class ReportReduce
    {
        static int _nCheckCalls = 0;

        /// <summary>
        /// return true if message needs Report
        /// </summary>
        /// <param name="Doc">
        /// 用来计算相似度的数据，根据计算结果维护词频
        /// </param>
        /// <returns></returns>
        public static bool CheckReportNeeded(string[] Doc)
        {
            try
            {
                if (Doc != null)
                {
                    Doc = Doc.Select(str => str.ToLower()).ToArray();

                    foreach(var strOut in Doc)
                    {
                        Console.Write( strOut + ",");
                    }

                    double dbCurTfd = TfIdfCalculator.Instance.CalculateMaxSimilarity(Doc);
                    double dbTargetTfd = TfIdfCalculator.Instance.GetCurTfIdfValue();

#if DEBUG
                    Console.WriteLine($"\n[CalculateMaxSimilarity]:[{dbTargetTfd}][{dbCurTfd}] -> {string.Join(" ", Doc)}");
#endif
                    _nCheckCalls++;
                    if (_nCheckCalls % 20 == 0)
                    {
                        if (_nCheckCalls > 0xFFFFFFF) _nCheckCalls = 0;
                        TfIdfCalculator.Instance.OptimizeDocumentFrequency();
                    }

                    if (dbCurTfd <= dbTargetTfd)
                    {
                        TfIdfCalculator.Instance.AddDocument(Doc);
                        return true;
                    }

                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CheckReportNeeded]: Exception:" + ex.ToString());

                return true;
            }
        }
    }
}
