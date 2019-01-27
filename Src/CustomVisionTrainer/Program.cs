using CommandLineParser.Exceptions;
using System;
using System.Threading.Tasks;

namespace CustomVisionTrainer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var parser = new CommandLineParser.CommandLineParser();
            var options = new ParsingOptions();

            try
            {
                parser.ExtractArgumentAttributes(options);
                parser.ParseCommandLine(args);
            }
            catch (CommandLineException e)
            {
                Console.WriteLine(e.Message);
                /*
                 * you can help the user by printing all the possible arguments and their
                 * description, CommandLineParser class can do this for you.
                 */
                parser.ShowUsage();
                return;
            }

            await Trainer.TrainAsync(options);

#if DEBUG
            Console.ReadLine();
#endif
        }
    }
}
