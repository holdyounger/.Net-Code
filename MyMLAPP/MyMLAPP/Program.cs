// See https://aka.ms/new-console-template for more information
// https://dotnet.microsoft.com/zh-cn/learn/ml-dotnet/get-started-tutorial/intro
using MyMLAPP;

class Program
{
    static void Main()
    {
        string input;

        // 使用while循环持续读取用户输入  
        while (true)
        {
            Console.Write("请输入一些文本（输入'exit'退出）: ");
            input = Console.ReadLine(); // 读取用户输入  

            // 检查用户是否输入了'exit'  
            if (input.ToLower() == "exit")
            {
                break; // 如果是，则退出循环  
            }

            // 在这里可以对输入进行处理，例如打印出来  
            Console.WriteLine("你输入了: " + input);

            // Add input data
            var sampleData = new SentimentModel.ModelInput()
            {
                Col0 = input.ToString()
            };

            // Load model and predict output of sample data
            var result = SentimentModel.Predict(sampleData);

            // If Prediction is 1, sentiment is "Positive"; otherwise, sentiment is "Negative"
            var sentiment = result.PredictedLabel == 1 ? "Positive" : "Negative";
            Console.WriteLine($"Text: {sampleData.Col0}\nSentiment: {sentiment}");

        }

        Console.WriteLine("程序已退出。");
    }
}