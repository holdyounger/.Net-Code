using DnrspEngine.Tools;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata;

public class TfIdfCalculator
{
    private List<string[]> _documents;
    private Dictionary<string, int> _documentFrequency;
    private int _totalDocuments;
    private static TfIdfCalculator _instance;

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
        _documents = new List<string[]>();
        _documentFrequency = new Dictionary<string, int>();
    }

    // 实现接口方法：添加文档
    public void AddDocument(string document)
    {
        var newDoc = document.Split(' ');
        _documents.Add(newDoc);
        _totalDocuments++;

        var uniqueTerms = new HashSet<string>(newDoc);
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
    public void OptimizeDocumentFrequency(int threshold)
    {
        var termsToRemove = new List<string>();
        foreach (var term in _documentFrequency.Keys.ToList())
        {
            if (_documentFrequency[term] < threshold)
            {
                termsToRemove.Add(term);
            }
        }

        foreach (var term in termsToRemove)
        {
            _documentFrequency.Remove(term);
        }

        // 移除文档中不再需要的词汇
        foreach (var doc in _documents)
        {
            for (int i = 0; i < doc.Length; i++)
            {
                if (termsToRemove.Contains(doc[i]))
                {
                    doc[i] = null; // 标记为待移除
                }
            }
        }

        // 实际移除文档中的空元素
        for (int i = 0; i < _documents.Count; i++)
        {
            _documents[i] = _documents[i].Where(x => x != null).ToArray();
        }

        // 移除空文档
        _documents.RemoveAll(doc => doc.Length == 0);
        _totalDocuments = _documents.Count;
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
    private double TermFrequency(string[] doc, string term)
    {
        return doc.Count(t => t == term) / (double)doc.Length;
    }

    // 计算逆文档频率（IDF），使用平滑公式
    private double InverseDocumentFrequency(string term)
    {
        if (!_documentFrequency.ContainsKey(term))
        {
            return 0;
        }
        return Math.Log((1 + _totalDocuments) / (1 + (double)_documentFrequency[term])) + 1;
    }

    // 计算 TF-IDF 值
    private double TfIdf(string[] doc, string term)
    {
        return TermFrequency(doc, term) * InverseDocumentFrequency(term);
    }

    // 计算文档的 TF-IDF 向量
    private Dictionary<string, double> GetTfIdfVector(string[] doc)
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

    // 计算余弦相似度
    private double CosineSimilarity(Dictionary<string, double> vector1, Dictionary<string, double> vector2)
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

        return dotProduct / (normA * normB);
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

public static class SimilarityHelper
{
    public static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0)
        {
            return m;
        }

        if (m == 0)
        {
            return n;
        }

        for (int i = 0; i <= n; d[i, 0] = i++)
        {
        }

        for (int j = 0; j <= m; d[0, j] = j++)
        {
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    public static bool IsSimilar(string s1, string s2, int threshold = 3)
    {
        int level = LevenshteinDistance(s1, s2);
        Console.WriteLine($"LevenshteinDistance:{level}");
        return level <= threshold;
    }
}

public static class CosineSimilarity
{
    public static double Calculate(string s1, string s2)
    {
        var vector1 = GetVector(s1);
        var vector2 = GetVector(s2);

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        foreach (var key in vector1.Keys.Union(vector2.Keys))
        {
            int a = vector1.ContainsKey(key) ? vector1[key] : 0;
            int b = vector2.ContainsKey(key) ? vector2[key] : 0;
            dotProduct += a * b;
            normA += Math.Pow(a, 2);
            normB += Math.Pow(b, 2);
        }

        normA = Math.Sqrt(normA);
        normB = Math.Sqrt(normB);

        if (normA == 0 || normB == 0)
            return 0;

        return dotProduct / (normA * normB);
    }

    private static Dictionary<string, int> GetVector(string text)
    {
        var words = text.Split(' ');
        var vector = new Dictionary<string, int>();
        foreach (var word in words)
        {
            if (vector.ContainsKey(word))
                vector[word]++;
            else
                vector[word] = 1;
        }
        return vector;
    }
}

class Program
{
    public static void TF_IF_TEST()
    {
        string filePath = "D:\\Documents\\A_Source\\Windows-API-Usage\\.Net-Code\\SimilarityHelper\\test.txt";
        try
        {
            string[] lines = File.ReadAllLines(filePath);
            List<string> documents = new List<string>(lines);

            // TfIdfCalculator.Instance.InitDocuments(documents.GetRange(0, 3));

            string doc = "a b c d";
            // TfIdfCalculator.Instance.AddDocument(doc);


            // double similarity = TfIdfCalculator.Instance.CalculateSimilarity(1, 3);

            // Console.WriteLine($"similarity:{similarity}");

            // 添加一个新文档

            string newDoc = "D:\\videoserver\\web\\ffmpeg\\ffmpeg.exe -i D:\\sp\\HNT_HNT_HQ25001778-2_2_638743439880376274.mp4 -vcodec copy -acodec copy D:\\videoserver\\web\\videos\\HNT_HNT_HQ25001778-2_2_638743439880376274.flv ";
            double calcTDf = TfIdfCalculator.Instance.CalculateMaxSimilarity(newDoc.Split(' '));
            if (calcTDf < 0.2)
            {
                TfIdfCalculator.Instance.AddDocument(newDoc);

            }

            double similarity = TfIdfCalculator.Instance.CalculateSimilarity(1, 3);
            Console.WriteLine($"similarity:{similarity}");

            // 优化文档频率，移除出现次数少于 2 的词汇
            TfIdfCalculator.Instance.OptimizeDocumentFrequency(2);

            similarity = TfIdfCalculator.Instance.CalculateSimilarity(1, 3);
            Console.WriteLine($"similarity:{similarity}");


            bool bFirst = true;
            for (int i = 0; i < documents.Count; i++)
            {
                Console.WriteLine($"[{i}]:" + documents[i]);
            }

            int nStart = -1;
            int nEnd = documents.Count > 30 ? documents.Count : documents.Count;
            for (; nStart < nEnd; nStart++)
            {
                for (int j = nStart + 1; j < nEnd; j++)
                {
                    if (nStart == -1)
                    {
                        if (bFirst)
                        {
                            bFirst = false;
                            Console.Write(" \t");
                        }

                        if (j + 1 < nEnd)
                        {
                            Console.Write((j + 1) + "\t");
                        }
                        continue;
                    }

                    if (j == nStart + 1)
                    {
                        Console.Write(nStart + "\t");
                    }

                    similarity = TfIdfCalculator.Instance.CalculateSimilarity(nStart, j);
                    Console.Write(similarity.ToString("F2") + "\t");

                }
                Console.Write("\n");
            }
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine("文件未找到，请检查文件路径。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"读取文件时发生错误: {ex.Message}");
        }
    }

    public static void TEST_REPORT()
    {
        do
        {
            string strDoc;

            Console.Write("请输入字符串: ");
            strDoc = Console.ReadLine();


            // 使用 String.Split 方法分割字符串

            if (strDoc != null)
            {
                char[] delimiters = new char[] { ' ', '-', ',', '\\', ';', '\t' };
                string[] words = strDoc.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

                // 检查是否输入 'exit' 以退出循环
                if (strDoc.ToLower() == "exit")
                {
                    break;
                }

                if(ReportReduce.CheckReportNeeded(words))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Report!!!");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Don't Report!!!");
                }
                Console.ForegroundColor = ConsoleColor.White;
            }

            // 提示用户输入第二个字符串

        } while (true);
    }
    static void Main(string[] args)
    {
        do
        {
            TEST_REPORT();

            string[] strDocs = { "ADC", "2RT" };
            string str1 = "3";

            strDocs = strDocs.Select(str => str.ToLower()).ToArray();

            List<string> listParas = strDocs.ToList();
            listParas.Add(("3"));

            string strDoc = string.Join(" ", listParas);



            string[] strDocs1 = null;
            Console.WriteLine(string.Join(" ", strDocs1));

            TF_IF_TEST();

            string s1 = "argv0:D:\\videoserver\\web\\ffmpeg\\ffmpeg.exe argv1:-i D:\\sp\\HNT_HNT_HQ25001778-2_2_638743439880376274.mp4 -vcodec copy -acodec copy D:\\videoserver\\web\\videos\\HNT_HNT_HQ25001778-2_2_638743439880376274.flv ";
            string s2 = "argv0:D:\\videoserver\\web\\ffmpeg\\ffmpeg.exe argv1:-i D:\\sp\\HNT_HNT_HQ25000651-3_3_638725315131384296.mp4 -vcodec copy -acodec copy D:\\videoserver\\web\\videos\\HNT_HNT_HQ25000651-3_3_638725315131384296.flv ";


            Console.WriteLine($"s1:{s1} \n s2:{s2}");

            bool bRet = SimilarityHelper.IsSimilar(s1, s2);
            double dSimly = CosineSimilarity.Calculate(s1, s2);

            Console.WriteLine($"是否相似：{bRet}, 余弦相似度：{dSimly}");

            // 提示用户输入第一个字符串
            Console.Write("请输入第一个字符串 (输入 'exit' 退出): ");
            s1 = Console.ReadLine();

            // 检查是否输入 'exit' 以退出循环
            if (s1.ToLower() == "exit")
            {
                break;
            }

            // 提示用户输入第二个字符串
            Console.Write("请输入第二个字符串: ");
            s2 = Console.ReadLine();

        } while (true);
    }
}