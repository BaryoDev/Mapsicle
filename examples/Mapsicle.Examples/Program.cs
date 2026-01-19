using Mapsicle.Examples;

Console.WriteLine();
Console.WriteLine("*".PadRight(60, '*'));
Console.WriteLine("*  MAPSICLE EXAMPLES                                       *");
Console.WriteLine("*  Demonstrating all packages in the Mapsicle ecosystem    *");
Console.WriteLine("*".PadRight(60, '*'));
Console.WriteLine();

// Run all example categories
CoreExamples.Run();
FluentExamples.Run();
ValidationExamples.Run();
NamingConventionExamples.Run();

Console.WriteLine("=".PadRight(60, '='));
Console.WriteLine("All examples completed successfully!");
Console.WriteLine("=".PadRight(60, '='));
