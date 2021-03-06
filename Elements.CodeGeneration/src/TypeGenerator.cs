using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;
using Elements.Generate.StringUtils;
using NJsonSchema.CodeGeneration;
using NJsonSchema.CodeGeneration.CSharp.Models;

namespace Elements.Generate
{
    class ElementsTypeNameGenerator : ITypeNameGenerator
    {
        // TODO(Ian): This type name generator is only required because njsonschema
        // calls dependencies 'Json2', 'Json3', etc. We use their title to label
        // them and then they get excluded by that title. This is fragile because
        // if a user gives their schema a title that is different than its id,
        // this will break. We need to figure out why njson schema has this bizarre
        // behavior. This behavior does not exist when the schemas are loaded
        // from disk, only when they are referenced by urls.
        public string Generate(JsonSchema schema, string typeNameHint, IEnumerable<string> reservedTypeNames)
        {
            // Console.WriteLine(typeNameHint + ":" + schema.InheritedSchema ?? "none");
            if (schema.IsEnumeration || String.IsNullOrEmpty(schema.Title))
            {
                return typeNameHint;
            }
            else
            {
                return schema.Title.ToSafeIdentifier();
            }
        }
    }

    class ElementsPropertyNameGenerator : IPropertyNameGenerator
    {
        public string Generate(JsonSchemaProperty property)
        {
            return property.Name.ToSafeIdentifier();
        }
    }

    /// <summary>
    /// TypeGenerator contains logic for generating element types from JSON schemas.
    /// </summary>
    public static class TypeGenerator
    {
        /// <summary>
        /// These are all the 'base' schemas defined for Elements.
        /// </summary>
        private static readonly string[] _hyparSchemas = new string[]{
                "https://hypar.io/Schemas/GeoJSON/Position.json",
                "https://hypar.io/Schemas/Geometry/Solids/Extrude.json",
                "https://hypar.io/Schemas/Geometry/Solids/Lamina.json",
                "https://hypar.io/Schemas/Geometry/Solids/SolidOperation.json",
                "https://hypar.io/Schemas/Geometry/Solids/Sweep.json",
                "https://hypar.io/Schemas/Geometry/Arc.json",
                "https://hypar.io/Schemas/Geometry/Color.json",
                "https://hypar.io/Schemas/Geometry/Curve.json",
                "https://hypar.io/Schemas/Geometry/Line.json",
                "https://hypar.io/Schemas/Geometry/Plane.json",
                "https://hypar.io/Schemas/Geometry/Polygon.json",
                "https://hypar.io/Schemas/Geometry/Polyline.json",
                "https://hypar.io/Schemas/Geometry/Profile.json",
                "https://hypar.io/Schemas/Geometry/Representation.json",
                "https://hypar.io/Schemas/Geometry/Transform.json",
                "https://hypar.io/Schemas/Geometry/Vector3.json",
                "https://hypar.io/Schemas/Properties/NumericProperty.json",
                "https://hypar.io/Schemas/GeometricElement.json",
                "https://hypar.io/Schemas/Element.json",
                "https://hypar.io/Schemas/Material.json",
                "https://hypar.io/Schemas/Model.json",
                "https://hypar.io/Schemas/Geometry/Matrix.json",
                "https://hypar.io/Schemas/InputData.json",
                "https://geojson.org/schema/Point.json",
            };

        private const string NAMESPACE_PROPERTY = "x-namespace";
        private static string[] _coreTypeNames;
        private static string _templatesPath;

        /// <summary>
        /// The directory in which to find code templates. Some execution contexts require this to be overriden as the
        /// Executing Assembly is not necessarily in the same place as the templates (e.g. Headless Grasshopper Execution)
        /// </summary>
        public static string TemplatesPath
        {
            get
            {
                if (_templatesPath == null)
                {
                    _templatesPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "./Templates"));
                }
                if (!Directory.Exists(_templatesPath))
                {
                    Console.WriteLine("Templates path attempted: " + _templatesPath);
                    throw new InvalidDataException("The templates folder cannot be found and is necessary for successful code generation. Be sure that the Hypar.Elements.CodeGeneration nuget package is referenced in all of your projects.");
                }
                return _templatesPath;
            }
            set => _templatesPath = value;
        }


        /// <summary>
        /// Generate a user-defined type in a .g.cs file from a schema.
        /// </summary>
        /// <param name="uri">The uri to the schema which defines the type. This can be a url or a relative file path.</param>
        /// <param name="outputBaseDir">The base output directory.</param>
        /// <param name="isUserElement">Is the type a user-defined element?</param>
        /// <returns>
        /// A GenerationResult object containing info about the success or failure of generation,
        /// the file path of the generated code, and any errors that may have occurred during generation.
        /// </returns>
        public static async Task<GenerationResult> GenerateUserElementTypeFromUriAsync(string uri, string outputBaseDir, bool isUserElement = false)
        {
            DotLiquid.Template.DefaultIsThreadSafe = true;
            DotLiquid.Template.RegisterFilter(typeof(HyparFilters));

            var schema = await GetSchemaAsync(uri);

            string ns;
            if (!GetNamespace(schema, out ns))
            {
                return new GenerationResult
                {
                    Success = false,
                    DiagnosticResults = new[] { "The provided schema does not contain the required 'x-namespace' property." }
                };
            }

            var typeName = schema.Title;
            if (_coreTypeNames == null)
            {
                _coreTypeNames = GetCoreTypeNames();
            }
            var excludedTypeNames = _coreTypeNames.Where(n => n != typeName).ToArray();
            return WriteTypeFromSchemaToDisk(schema, outputBaseDir, typeName, ns, isUserElement, excludedTypeNames);
        }

        /// <summary>
        /// Generate user-defined types in .g.cs files from a schema.
        /// </summary>
        /// <param name="uris">An array of uris.</param>
        /// <param name="outputBaseDir">The base output directory.</param>
        /// <param name="isUserElement">Is the type a user-defined element?</param>
        public static async Task<GenerationResult[]> GenerateUserElementTypesFromUrisAsync(string[] uris, string outputBaseDir, bool isUserElement = false)
        {
            DotLiquid.Template.DefaultIsThreadSafe = true;
            DotLiquid.Template.RegisterFilter(typeof(HyparFilters));
            var results = new List<Task<GenerationResult>>();
            foreach (var uri in uris)
            {
                results.Add(GenerateUserElementTypeFromUriAsync(uri, outputBaseDir, isUserElement));
            }
            var allResults = await Task.WhenAll(results);
            return allResults;
        }


        /// <summary>
        /// Generate an in-memory assembly containing all the types generated from the supplied uris.
        /// </summary>
        /// <param name="uris">A collection of uris to JSON schema. These can be public urls or relative file paths.</param>
        /// <param name="frameworkBuild">If true, the assembly will be built against the .NET framework, otherwise it will be built against .NET core.</param>
        /// <returns>A CompilationResult containing information about the compilation.</returns>
        public static async Task<CompilationResult> GenerateInMemoryAssemblyFromUrisAndLoadAsync(string[] uris, bool frameworkBuild = false)
        {
            DotLiquid.Template.DefaultIsThreadSafe = true;
            DotLiquid.Template.RegisterFilter(typeof(HyparFilters));
            // https://docs.microsoft.com/en-us/archive/msdn-magazine/2017/may/net-core-cross-platform-code-generation-with-roslyn-and-net-core
            var code = new Dictionary<string, string>();
            foreach (var uri in uris)
            {
                // TODO: We can refactor this inner loop to share the code
                // with the GenerateInMemoryAssemblyFromUrisAndSaveAsync method.
                // We didn't do this originally because the return value of the refactored
                // method would need to be a CompilationResult and the loop would generate a List<string>,
                // but because it's async we can't do that as a ref parameter.
                try
                {
                    var schema = await GetSchemaAsync(uri);
                    var csharpFileContents = GenerateCSharpCodeForSchema(schema);
                    foreach (var csharp in csharpFileContents)
                    {
                        if (code.ContainsKey(csharp.Key))
                        {
                            continue;
                        }
                        code[csharp.Key] = csharp.Value;
                    }
                }
                catch (Exception ex)
                {
                    var diagnostics = new[]
                    {
                        $"There was an error reading the schema at {uri}: {ex.Message}."
                    };
                    return new CompilationResult
                    {
                        Success = false,
                        DiagnosticResults = diagnostics
                    };
                }
            }

            var compilation = GenerateCompilation(code.Values.ToList(), frameworkBuild: frameworkBuild);

            if (TryEmitAndLoad(compilation, out Assembly assembly, out string[] diagnosticResults))
            {
                return new CompilationResult
                {
                    Success = true,
                    DiagnosticResults = diagnosticResults,
                    Assembly = assembly
                };
            }
            else
            {
                return new CompilationResult
                {
                    Success = false,
                    DiagnosticResults = diagnosticResults,
                    Assembly = null
                };
            }
        }

        /// <summary>
        /// Generate an in-memory assembly containing all the types generated from the supplied uris and save it to disk.
        /// </summary>
        /// <param name="uris">A collection of uris to JSON schema. These can be public urls or relative file paths.</param>
        /// <param name="dllPath">The path at which the dll will be written. If this is not null, the assembly will be written but not loaded.</param>
        /// <param name="frameworkBuild">If true, the assembly will be built against the .NET framework, otherwise it will be built against .NET core.</param>
        /// <returns>A CompilationResult containing information about the compilation.</returns>
        public static async Task<CompilationResult> GenerateInMemoryAssemblyFromUrisAndSaveAsync(string[] uris, string dllPath, bool frameworkBuild = false)
        {
            DotLiquid.Template.DefaultIsThreadSafe = true;
            DotLiquid.Template.RegisterFilter(typeof(HyparFilters));
            // https://docs.microsoft.com/en-us/archive/msdn-magazine/2017/may/net-core-cross-platform-code-generation-with-roslyn-and-net-core

            var code = new Dictionary<string, string>();
            foreach (var uri in uris)
            {
                try
                {
                    var schema = await GetSchemaAsync(uri);
                    var csharpFileContents = GenerateCSharpCodeForSchema(schema);
                    foreach (var csharp in csharpFileContents)
                    {
                        if (code.ContainsKey(csharp.Key))
                        {
                            continue;
                        }
                        code[csharp.Key] = csharp.Value;
                    }
                }
                catch (Exception ex)
                {
                    var diagnostics = new[]
                    {
                        $"There was an error reading the schema at {uri}: {ex.Message}."
                    };
                    return new CompilationResult
                    {
                        Success = false,
                        DiagnosticResults = diagnostics
                    };
                }
            }

            var compilation = GenerateCompilation(code.Values.ToList(), frameworkBuild: frameworkBuild);

            if (TryEmitAndSave(compilation, dllPath, out string[] diagnosticResults))
            {
                return new CompilationResult
                {
                    Success = true,
                    DiagnosticResults = diagnosticResults,
                };
            }
            else
            {
                return new CompilationResult
                {
                    Success = false,
                    DiagnosticResults = diagnosticResults,
                };
            }
        }

        /// <summary>
        /// Generate the core element types as .cs files to the specified output directory.
        /// </summary>
        /// <param name="outputBaseDir">The root directory into which generated files will be written.</param>
        public static async Task<GenerationResult[]> GenerateElementTypesAsync(string outputBaseDir)
        {
            DotLiquid.Template.DefaultIsThreadSafe = true;
            DotLiquid.Template.RegisterFilter(typeof(HyparFilters));
            var typeNames = _hyparSchemas.Select(u => GetTypeNameFromSchemaUri(u)).ToList();
            var tasks = new List<Task<GenerationResult>>();
            foreach (var uri in _hyparSchemas)
            {
                var split = uri.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries).Skip(3);
                var outDir = Path.Combine(outputBaseDir, string.Join("/", split.Take(split.Count() - 1)).TrimEnd('.'));
                if (!Directory.Exists(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                tasks.Add(GenerateUserElementTypeFromUriAsync(uri, outDir));
            }
            var allResults = await Task.WhenAll(tasks);
            return allResults;
        }

        /// <summary>
        /// Get a list of the core Hypar types, which should be excluded from code generation.
        /// </summary>
        public static string[] GetCoreTypeNames()
        {
            return _hyparSchemas.Select(u => GetTypeNameFromSchemaUri(u)).ToArray();
        }

        private static string GetTypeNameFromSchemaUri(string uri)
        {
            return Path.GetFileNameWithoutExtension(uri.Split(new[] { "/" }, StringSplitOptions.RemoveEmptyEntries).Last());
        }

        private static string GetFileNameFromTypeName(string typeName)
        {
            return $"{typeName}.g.cs";
        }

        /// <summary>
        /// Asynchronously load a JSON Schema from a URI. If a web address is provided,
        /// it will be loaded from the URL, otherwise it will attempt to load from disk.
        /// </summary>
        /// <param name="uri"></param>
        public static async Task<JsonSchema> GetSchemaAsync(string uri)
        {
            if (uri.StartsWith("http://") || uri.StartsWith("https://"))
            {
                return await JsonSchema.FromUrlAsync(uri);
            }
            else
            {
                var path = Path.GetFullPath(Path.Combine(Assembly.GetExecutingAssembly().Location, uri));
                if (!File.Exists(path))
                {
                    throw new Exception($"The specified schema, {uri}, can not be found as a relative file or a url.");
                }
                return await JsonSchema.FromJsonAsync(File.ReadAllText(path));
            }
        }

        private static bool GetNamespace(JsonSchema schema, out string @namespace)
        {
            if (!schema.ExtensionData.ContainsKey(NAMESPACE_PROPERTY))
            {
                Console.WriteLine($"The provided schema does not contain the required 'x-namespace' property.");
                @namespace = null;
                return false;
            }
            @namespace = (string)schema.ExtensionData[NAMESPACE_PROPERTY];
            return true;
        }

        private static Dictionary<string, string> GetCodeForTypesFromSchema(JsonSchema schema, string typeName, string ns, bool isUserElement = false, string[] excludedTypes = null)
        {
            var templates = TemplatesPath;

            var structTypes = new[] { "Color", "Vector3" };

            // A limited set of the solid operation types. This will be used
            // to add INotifyPropertyChanged logic, so we don't add the
            // base class SolidOperation, or the Import class.
            var solidOpTypes = new[] { "Extrude", "Sweep", "Lamina" };

            var settings = new CSharpGeneratorSettings()
            {
                Namespace = ns,
                ArrayType = "IList",
                ArrayInstanceType = "List",
                ExcludedTypeNames = excludedTypes == null ? new string[] { } : excludedTypes,
                TemplateDirectory = templates,
                GenerateJsonMethods = false,
                ClassStyle = solidOpTypes.Contains(typeName) ? CSharpClassStyle.Inpc : CSharpClassStyle.Poco,
                TypeNameGenerator = new ElementsTypeNameGenerator(),
                PropertyNameGenerator = new ElementsPropertyNameGenerator(),
            };

            var generator = new CSharpGenerator(schema, settings);

            var typeFiles = new Dictionary<string, string>();
            // We still need this call to GenerateFile() even though we don't use the file's
            // text.  It is already a documented issue https://github.com/RicoSuter/NJsonSchema/issues/893
            var file = generator.GenerateFile();
            var typeArtifacts = generator.GenerateTypes();

            foreach (var fileArtifact in typeArtifacts)
            {
                if (String.IsNullOrWhiteSpace(fileArtifact.Code))
                {
                    continue;
                }
                // strategy for using templates taken from the NJsonSchema source
                // https://github.com/RicoSuter/NJsonSchema/blob/master/src/NJsonSchema.CodeGeneration.CSharp/CSharpGenerator.cs#L50
                var model = new FileTemplateModel
                {
                    Namespace = settings.Namespace ?? string.Empty,
                    TypesCode = fileArtifact.Code
                };
                var template = settings.TemplateFactory.CreateTemplate("CSharp", "File", model);
                var code = template.Render();
                var fileContents = FileTweaksAndCleanup(typeName, isUserElement, structTypes, code);
                typeFiles[fileArtifact.TypeName] = fileContents;
            }

            return typeFiles;
        }

        private static string FileTweaksAndCleanup(string typeName, bool isUserElement, string[] structTypes, string file)
        {
            if (isUserElement)
            {
                // remove unncessary imports
                // TODO: make this a conditional for the code generation using ExtensionData, instead of using string replacement.
                // For whatever reason, this was not working with code in File.liquid — only Class.liquid.
                file = file.Replace(@"
using Hypar.Functions;
using Hypar.Functions.Execution;
using Hypar.Functions.Execution.AWS;", "");
                // Insert the UserElement attribute directly before
                // 'public partial class ' any time it occurs in the file
                // because we may be generating code for multiple user types in
                // the same file.
                var start = 0;
                while (true)
                {
                    start = file.IndexOf($"public partial class ", start);
                    if (start == -1)
                    {
                        break;
                    }
                    var userElementAttribute = $"[UserElement]\n\t";
                    file = file.Insert(start, userElementAttribute);
                    start += userElementAttribute.Length + 1;  // increment chars to get past the recent insertion
                }
            }

            if (typeName == "Model")
            {
                // JSON schema only allows us to generate Dictionary<string,Element>
                // Replace those entries here with Dictionary<Guid,Element>.
                file = file.Replace("System.Collections.Generic.IDictionary<string, Element>", "System.Collections.Generic.IDictionary<Guid, Element>");
                file = file.Replace("System.Collections.Generic.Dictionary<string, Element>", "System.Collections.Generic.Dictionary<Guid, Element>");

                // Obsolete the origin property on Model.
                file = file.Replace("public Position Origin { get; set; }", "[Obsolete(\"Use Transform instead.\")]\n\t\tpublic Position Origin { get; set; }");
            }
            // Convert some classes to structs.
            else if (structTypes.Contains(typeName))
            {
                file = file.Replace($"public partial class {typeName}", $"public partial struct {typeName}");
            }

            return file;
        }


        // file lock test taken from https://stackoverflow.com/questions/876473/is-there-a-way-to-check-if-a-file-is-in-use
        private static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }

            //file is not locked
            return false;
        }

        private static GenerationResult WriteTypeFromSchemaToDisk(JsonSchema schema, string outDirPath, string typeName, string ns, bool isUserElement = false, string[] excludedTypes = null)
        {
            var diagnosticMessages = new List<string>();
            Console.WriteLine($"Generating type {@ns}.{typeName} in {outDirPath}...");
            var typeCodeDict = GetCodeForTypesFromSchema(schema, typeName, ns, isUserElement, excludedTypes);
            foreach (var kvp in typeCodeDict)
            {
                var path = Path.Combine(outDirPath, $"{kvp.Key}.g.cs");
                if (File.Exists(path))
                {
                    // need to wait for file to be available because code gen is async
                    while (IsFileLocked(new FileInfo(path)))
                    {
                        System.Threading.Thread.Sleep(100);
                        continue;
                    }
                    var existing = File.ReadAllText(path);
                    if (existing != kvp.Value)
                    {
                        Console.WriteLine($"There were two versions of code generated for the type {kvp.Key}");
                    }
                    continue;
                }
                try
                {
                    File.WriteAllText(path, kvp.Value);
                }
                catch (IOException ioe)
                {
                    diagnosticMessages.Add($"{typeName} failed to write, possibly because it was already being written to disk.");
                }
            }
            return new GenerationResult
            {
                Success = true,
                FilePath = outDirPath,
                DiagnosticResults = new string[0]
            };
        }

        /// <summary>
        /// Get the currently loaded UserElement types
        /// </summary>
        /// <param name="userElementTypesOnly">If true, only return types with the UserElement attribute.</param>
        /// <returns>A list of the loaded types with the UserElement attribute.</returns>
        public static List<Type> GetLoadedElementTypes(bool userElementTypesOnly = false)
        {
            var loadedTypes = new List<Type>();
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            Func<Type, bool> IsUserElement = t => t.GetCustomAttributes(typeof(UserElement), true).Length > 0;
            Func<Type, bool> IsElement = t => typeof(Element).IsAssignableFrom(t);
            var typeFilter = userElementTypesOnly ? IsUserElement : IsElement;
            foreach (var asm in asms)
            {
                try
                {
                    var userTypes = asm.GetTypes().Where(typeFilter);
                    foreach (var ut in userTypes)
                    {
                        loadedTypes.Add(ut);
                    }
                }
                catch
                {
                    continue;
                }
            }
            return loadedTypes;
        }

        /// <summary>
        /// For a given schema, generate code, compile an assembly, and write it to disk at the specified path.
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="dllPath"></param>
        /// <param name="diagnosticResults"></param>
        /// <param name="frameworkBuild"></param>
        /// <returns>Returns true if the dll was generated successfully, otherwise false.</returns>
        public static bool GenerateAndSaveDllForSchema(JsonSchema schema, string dllPath, out string[] diagnosticResults, bool frameworkBuild = false)
        {
            var csharpFileContents = GenerateCSharpCodeForSchema(schema);
            if (csharpFileContents == null)
            {
                diagnosticResults = new string[] { };
                return false;
            }
            var compilation = GenerateCompilation(csharpFileContents.Select(kvp => kvp.Value).ToList(), schema.Title, frameworkBuild);
            return TryEmitAndSave(compilation, dllPath, out diagnosticResults);
        }

        private static Dictionary<string, string> GenerateCSharpCodeForSchema(JsonSchema schema)
        {
            string ns;
            if (!GetNamespace(schema, out ns))
            {
                return null;
            }

            var typeName = schema.Title;
            if (_coreTypeNames == null)
            {
                _coreTypeNames = GetCoreTypeNames();
            }

            var loadedTypes = GetLoadedElementTypes(true).Select(t => t.Name);
            if (loadedTypes.Contains(typeName))
            {
                return new Dictionary<string, string>();
            }
            var localExcludes = _coreTypeNames.Where(n => n != typeName).ToArray();

            return GetCodeForTypesFromSchema(schema, typeName, ns, true, localExcludes);
        }

        private static CSharpCompilation GenerateCompilation(List<string> code, string compilationName = "UserElements", bool frameworkBuild = false)
        {
            // Generate the assembly from the various code files.
            var options = new CSharpParseOptions(LanguageVersion.CSharp7_3,
                                                 kind: Microsoft.CodeAnalysis.SourceCodeKind.Regular,
                                                 documentationMode: Microsoft.CodeAnalysis.DocumentationMode.Diagnose);
            var syntaxTrees = new List<Microsoft.CodeAnalysis.SyntaxTree>();
            foreach (var cs in code)
            {
                var tree = CSharpSyntaxTree.ParseText(cs, options);
                syntaxTrees.Add(tree);
            }

            var assemblyPath = frameworkBuild ? @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319" : Path.GetDirectoryName(typeof(object).Assembly.Location);
            var elementsAssemblyPath = Path.GetDirectoryName(typeof(Model).Assembly.Location);
            var newtonSoftPath = Path.GetDirectoryName(typeof(JsonConverter).Assembly.Location);

            IEnumerable<MetadataReference> defaultReferences = new[]
            {
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "netstandard.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.ComponentModel.Annotations.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Diagnostics.Tools.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.Serialization.Primitives.dll")),
                MetadataReference.CreateFromFile(Path.Combine(elementsAssemblyPath, "Hypar.Elements.dll")),
                MetadataReference.CreateFromFile(Path.Combine(newtonSoftPath, "Newtonsoft.Json.dll"))
            };

            // If we're building in a .net framework context, we need a different set of reference DLLs
            if (frameworkBuild)
            {
                defaultReferences = defaultReferences.Union(new[]
                {
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "mscorlib.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Core.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Linq.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.ObjectModel.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Linq.Expressions.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.Extensions.dll")),
                    MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.ComponentModel.DataAnnotations.dll")),
                });
            }
            else
            {
                defaultReferences = defaultReferences.Union(new[] { MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Private.CoreLib.dll")) });
            }


            var compileOptions = new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary,
                                                              optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Release);
            return CSharpCompilation.Create(compilationName,
                                                       syntaxTrees,
                                                       defaultReferences,
                                                       compileOptions);
        }

        private static bool TryEmitAndSave(CSharpCompilation compilation, string outputPath, out string[] diagnosticMessages)
        {
            var emitResult = compilation.Emit(outputPath);
            diagnosticMessages = emitResult.Diagnostics.Select(d => d.ToString()).ToArray();
            if (emitResult.Success == false)
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                return false;
            }
            else
            {
                return true;
            }
        }

        private static bool TryEmitAndLoad(CSharpCompilation compilation, out Assembly assembly, out string[] diagnosticMessages)
        {
            using (var ms = new MemoryStream())
            {
                var emitResult = compilation.Emit(ms);
                diagnosticMessages = emitResult.Diagnostics.Select(d => d.ToString()).ToArray();
                if (emitResult.Success)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    assembly = Assembly.Load(ms.ToArray());
                    return true;
                }
                else
                {
                    assembly = null;
                    return false;
                }
            }
        }
    }
}