[assembly: TUnit.Core.Executors.TestExecutor<Kurrent.Testkit.TestkitTestExecutor>]

[assembly: SkipAll("Kurrent.Surge.AI.HuggingFace.Tests", "Depends on a live Hugging Face model download")]
