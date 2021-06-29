using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Dalamud.Plugin;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using Penumbra.Importer.Models;
using Penumbra.Models;
using Penumbra.Util;

namespace Penumbra.Importer
{
    internal class TexToolsImport
    {
        private readonly DirectoryInfo _outDirectory;

        private const    string TempFileName = "textools-import";
        private readonly string _resolvedTempFilePath;

        public ImporterState State { get; private set; }

        public long TotalProgress { get; private set; }
        public long CurrentProgress { get; private set; }

        public float Progress
        {
            get
            {
                if( CurrentProgress != 0 )
                {
                    // ReSharper disable twice RedundantCast
                    return ( float )CurrentProgress / ( float )TotalProgress;
                }

                return 0;
            }
        }

        public string? CurrentModPack { get; private set; }

        public TexToolsImport( DirectoryInfo outDirectory )
        {
            _outDirectory         = outDirectory;
            _resolvedTempFilePath = Path.Combine( _outDirectory.FullName, TempFileName );
        }

        public void ImportModPack( FileInfo modPackFile )
        {
            CurrentModPack = modPackFile.Name;

            VerifyVersionAndImport( modPackFile );

            State = ImporterState.Done;
        }

        private void WriteZipEntryToTempFile( Stream s )
        {
            var fs = new FileStream( _resolvedTempFilePath, FileMode.Create );
            s.CopyTo( fs );
            fs.Close();
        }

        // You can in no way rely on any file paths in TTMPs so we need to just do this, sorry
        private static ZipEntry? FindZipEntry( ZipFile file, string fileName )
        {
            for( var i = 0; i < file.Count; i++ )
            {
                var entry = file[ i ];

                if( entry.Name.Contains( fileName ) )
                {
                    return entry;
                }
            }

            return null;
        }

        private PenumbraSqPackStream GetMagicSqPackDeleterStream( ZipFile file, string entryName )
        {
            State = ImporterState.WritingPackToDisk;

            // write shitty zip garbage to disk
            var entry = FindZipEntry( file, entryName );
            if( entry == null )
            {
                throw new FileNotFoundException( $"ZIP does not contain a file named {entryName}." );
            }

            using var s = file.GetInputStream( entry );

            WriteZipEntryToTempFile( s );

            var fs = new FileStream( _resolvedTempFilePath, FileMode.Open );
            return new MagicTempFileStreamManagerAndDeleterFuckery( fs );
        }

        private void VerifyVersionAndImport( FileInfo modPackFile )
        {
            using var zfs              = modPackFile.OpenRead();
            using var extractedModPack = new ZipFile( zfs );

            var mpl = FindZipEntry( extractedModPack, "TTMPL.mpl" );
            if( mpl == null )
            {
                throw new FileNotFoundException( "ZIP does not contain a TTMPL.mpl file." );
            }

            var modRaw = GetStringFromZipEntry( extractedModPack, mpl, Encoding.UTF8 );

            // At least a better validation than going by the extension.
            if( modRaw.Contains( "\"TTMPVersion\":" ) )
            {
                if( modPackFile.Extension != ".ttmp2" )
                {
                    PluginLog.Warning( $"File {modPackFile.FullName} seems to be a V2 TTMP, but has the wrong extension." );
                }

                ImportV2ModPack( modPackFile, extractedModPack, modRaw );
            }
            else
            {
                if( modPackFile.Extension != ".ttmp" )
                {
                    PluginLog.Warning( $"File {modPackFile.FullName} seems to be a V1 TTMP, but has the wrong extension." );
                }

                ImportV1ModPack( modPackFile, extractedModPack, modRaw );
            }
        }

        private void ImportV1ModPack( FileInfo modPackFile, ZipFile extractedModPack, string modRaw )
        {
            PluginLog.Log( "    -> Importing V1 ModPack" );

            var modListRaw = modRaw.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            );

            var modList = modListRaw.Select( JsonConvert.DeserializeObject< SimpleMod > );

            // Create a new ModMeta from the TTMP modlist info
            var modMeta = new ModMeta
            {
                Author      = "Unknown",
                Name        = modPackFile.Name,
                Description = "Mod imported from TexTools mod pack",
            };

            // Open the mod data file from the modpack as a SqPackStream
            using var modData = GetMagicSqPackDeleterStream( extractedModPack, "TTMPD.mpd" );

            var newModFolder = CreateModFolder( _outDirectory, Path.GetFileNameWithoutExtension( modPackFile.Name ) );

            File.WriteAllText(
                Path.Combine( newModFolder.FullName, "meta.json" ),
                JsonConvert.SerializeObject( modMeta )
            );

            ExtractSimpleModList( newModFolder, modList, modData );
        }

        private void ImportV2ModPack( FileInfo modPackFile, ZipFile extractedModPack, string modRaw )
        {
            var modList = JsonConvert.DeserializeObject< SimpleModPack >( modRaw );

            if( modList?.TTMPVersion == null )
            {
                PluginLog.Error( "Could not extract V2 Modpack. No version given." );
                return;
            }

            if( modList.TTMPVersion.EndsWith( "s" ) )
            {
                ImportSimpleV2ModPack( extractedModPack, modList );
                return;
            }

            if( modList.TTMPVersion.EndsWith( "w" ) )
            {
                ImportExtendedV2ModPack( extractedModPack, modRaw );
            }
        }

        public static DirectoryInfo CreateModFolder( DirectoryInfo outDirectory, string modListName )
        {
            var correctedPath = Path.Combine( outDirectory.FullName,
                Path.GetFileName( modListName ).RemoveInvalidPathSymbols().RemoveNonAsciiSymbols() );
            var newModFolder = new DirectoryInfo( correctedPath );
            var i            = 2;
            while( newModFolder.Exists && i < 12 )
            {
                newModFolder = new DirectoryInfo( correctedPath + $" ({i++})" );
            }

            if( newModFolder.Exists )
            {
                throw new IOException( "Could not create mod folder: too many folders of the same name exist." );
            }

            newModFolder.Create();
            return newModFolder;
        }

        private void ImportSimpleV2ModPack( ZipFile extractedModPack, SimpleModPack modList )
        {
            PluginLog.Log( "    -> Importing Simple V2 ModPack" );

            // Create a new ModMeta from the TTMP modlist info
            var modMeta = new ModMeta
            {
                Author = modList.Author ?? "Unknown",
                Name   = modList.Name   ?? "New Mod",
                Description = string.IsNullOrEmpty( modList.Description )
                    ? "Mod imported from TexTools mod pack"
                    : modList.Description!,
            };

            // Open the mod data file from the modpack as a SqPackStream
            using var modData = GetMagicSqPackDeleterStream( extractedModPack, "TTMPD.mpd" );

            var newModFolder = CreateModFolder( _outDirectory, modList.Name ?? "New Mod" );

            File.WriteAllText( Path.Combine( newModFolder.FullName, "meta.json" ),
                JsonConvert.SerializeObject( modMeta ) );

            ExtractSimpleModList( newModFolder, modList.SimpleModsList ?? Enumerable.Empty< SimpleMod >(), modData );
        }

        private void ImportExtendedV2ModPack( ZipFile extractedModPack, string modRaw )
        {
            PluginLog.Log( "    -> Importing Extended V2 ModPack" );

            var modList = JsonConvert.DeserializeObject< ExtendedModPack >( modRaw );

            // Create a new ModMeta from the TTMP modlist info
            var modMeta = new ModMeta
            {
                Author = modList.Author ?? "Unknown",
                Name   = modList.Name   ?? "New Mod",
                Description = string.IsNullOrEmpty( modList.Description )
                    ? "Mod imported from TexTools mod pack"
                    : modList.Description ?? "",
                Version = modList.Version ?? "",
            };

            // Open the mod data file from the modpack as a SqPackStream
            using var modData = GetMagicSqPackDeleterStream( extractedModPack, "TTMPD.mpd" );

            var newModFolder = CreateModFolder( _outDirectory, modList.Name ?? "New Mod" );

            if( modList.SimpleModsList != null )
            {
                ExtractSimpleModList( newModFolder, modList.SimpleModsList, modData );
            }

            if( modList.ModPackPages == null )
            {
                return;
            }

            // Iterate through all pages
            foreach( var page in modList.ModPackPages )
            {
                if( page.ModGroups == null )
                {
                    continue;
                }

                foreach( var group in page.ModGroups.Where( group => group.GroupName != null && group.OptionList != null ) )
                {
                    var groupFolder = new DirectoryInfo( Path.Combine( newModFolder.FullName, group.GroupName!.ReplaceInvalidPathSymbols().RemoveNonAsciiSymbols( ) ) );
                    if( groupFolder.Exists )
                    {
                        groupFolder     =  new DirectoryInfo( groupFolder.FullName + $" ({page.PageIndex})" );
                        group.GroupName += $" ({page.PageIndex})";
                    }

                    foreach( var option in group.OptionList!.Where( option => option.Name != null && option.ModsJsons != null ) )
                    {
                        var optionFolder = new DirectoryInfo( Path.Combine( groupFolder.FullName, option.Name!.ReplaceInvalidPathSymbols().RemoveNonAsciiSymbols() ) );
                        ExtractSimpleModList( optionFolder, option.ModsJsons!, modData );
                    }

                    AddMeta( newModFolder, groupFolder, group, modMeta );
                }
            }

            File.WriteAllText(
                Path.Combine( newModFolder.FullName, "meta.json" ),
                JsonConvert.SerializeObject( modMeta, Formatting.Indented )
            );
        }

        private static void AddMeta( DirectoryInfo baseFolder, DirectoryInfo groupFolder, ModGroup group, ModMeta meta )
        {
            var inf = new OptionGroup
            {
                SelectionType = group.SelectionType,
                GroupName     = group.GroupName!,
                Options       = new List< Option >(),
            };
            foreach( var opt in group.OptionList! )
            {
                var option = new Option
                {
                    OptionName  = opt.Name!,
                    OptionDesc  = string.IsNullOrEmpty( opt.Description ) ? "" : opt.Description!,
                    OptionFiles = new Dictionary< RelPath, HashSet< GamePath > >(),
                };
                var optDir = new DirectoryInfo( Path.Combine( groupFolder.FullName, opt.Name!.ReplaceInvalidPathSymbols().RemoveNonAsciiSymbols() ) );
                if( optDir.Exists )
                {
                    foreach( var file in optDir.EnumerateFiles( "*.*", SearchOption.AllDirectories ) )
                    {
                        option.AddFile( new RelPath( file, baseFolder ), new GamePath( file, optDir ) );
                    }
                }

                inf.Options.Add( option );
            }

            meta.Groups.Add( inf.GroupName, inf );
        }

        private void ImportMetaModPack( FileInfo file )
        {
            throw new NotImplementedException();
        }

        private void ExtractSimpleModList( DirectoryInfo outDirectory, IEnumerable< SimpleMod > mods, PenumbraSqPackStream dataStream )
        {
            State = ImporterState.ExtractingModFiles;

            // haha allocation go brr
            var wtf = mods.ToList();

            TotalProgress += wtf.LongCount();

            // Extract each SimpleMod into the new mod folder
            foreach( var simpleMod in wtf.Where( M => M != null ) )
            {
                ExtractMod( outDirectory, simpleMod, dataStream );
                CurrentProgress++;
            }
        }

        private void ExtractMod( DirectoryInfo outDirectory, SimpleMod mod, PenumbraSqPackStream dataStream )
        {
            PluginLog.Log( "        -> Extracting {0} at {1}", mod.FullPath, mod.ModOffset.ToString( "X" ) );

            try
            {
                var data = dataStream.ReadFile< PenumbraSqPackStream.PenumbraFileResource >( mod.ModOffset );

                var extractedFile = new FileInfo( Path.Combine( outDirectory.FullName, mod.FullPath! ) );
                extractedFile.Directory?.Create();

                if( extractedFile.FullName.EndsWith( "mdl" ) )
                {
                    ProcessMdl( data.Data );
                }

                File.WriteAllBytes( extractedFile.FullName, data.Data );
            }
            catch( Exception ex )
            {
                PluginLog.LogError( ex, "Could not extract mod." );
            }
        }

        private void ProcessMdl( byte[] mdl )
        {
            // Model file header LOD num
            mdl[ 64 ] = 1;

            // Model header LOD num
            var stackSize            = BitConverter.ToUInt32( mdl, 4 );
            var runtimeBegin         = stackSize    + 0x44;
            var stringsLengthOffset  = runtimeBegin + 4;
            var stringsLength        = BitConverter.ToUInt32( mdl, ( int )stringsLengthOffset );
            var modelHeaderStart     = stringsLengthOffset + stringsLength + 4;
            var modelHeaderLodOffset = 22;
            mdl[ modelHeaderStart + modelHeaderLodOffset ] = 1;
        }

        private static Stream GetStreamFromZipEntry( ZipFile file, ZipEntry entry )
            => file.GetInputStream( entry );

        private static string GetStringFromZipEntry( ZipFile file, ZipEntry entry, Encoding encoding )
        {
            using var ms = new MemoryStream();
            using var s  = GetStreamFromZipEntry( file, entry );
            s.CopyTo( ms );
            return encoding.GetString( ms.ToArray() );
        }
    }
}