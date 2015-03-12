﻿namespace Tfs.Build.Paket

module PaketHelpers =
    open Tfs.Build.Paket.Utils
    open Tfs.Build.Paket.GitHub

    let getDepsFile sourceDir =
        getFilesRec sourceDir "paket.dependencies" |> Seq.tryHead

    let getRefsFiles sourceDir =
        getFilesRec sourceDir "paket.references" |> List.ofArray
        

    let downloadLatestFromGitHub token destinationFileName logMessage logError =
        try
            System.IO.Path.GetDirectoryName(destinationFileName)
            |> ensureDir 

            let client = createClient token
            
            logMessage("fetching paket bootstrapper url")
            let latestBootstrapperUrl = 
                client |> 
                getBootstrapperUrl true  
                |> Async.RunSynchronously
            logMessage(sprintf "paket found at %s" latestBootstrapperUrl)
            
            downloadAndSave latestBootstrapperUrl destinationFileName 
            |> Async.RunSynchronously

            logMessage("installing paket.exe")
        with 
        | ex -> 
            logError(ex.ToString())

    let restoreFromSourceDir sourceDir logErrFn logMsgFn =
        if sourceDir |> (System.IO.Directory.Exists >> not) then 
            logErrFn "source directory not present"
            false
        else 
            let depsFile = sourceDir |> getDepsFile
            let referencesFiles = sourceDir |> getRefsFiles
            try 
                match depsFile, referencesFiles with
                | None, _ -> 
                    logErrFn "no paket.dependencies file found. aborting restore."
                    false
                | Some deps, refs ->
                    logMsgFn "found paket.dependencies and references files. restoring now"
                    Paket.RestoreProcess.Restore(deps, true, refs)
                    logMsgFn "restore complete"
                    true
            with
            | ex -> 
                ex.ToString() |> logErrFn
                false

    let getLockFileDeps sourceDir =
        sourceDir 
        |> getDepsFile
        |> Option.map Paket.DependenciesFile.FindLockfile
        |> Option.map (fun fi -> Paket.LockFile.LoadFrom fi.FullName)

    let nugetPackages sourceDir =
        match getLockFileDeps sourceDir with
        | None -> List.empty
        | Some lock -> lock.ResolvedPackages |> Map.toList

    let hasPrereleases sourceDir logErrFn logMsgFn =
        match nugetPackages sourceDir |> List.filter (fun (n,p) -> p.Version.PreRelease.IsSome) with
        | [] -> 
            logMsgFn "No prereleases found"
            false
        | prereleases -> 
            prereleases
            |> List.map (fun (n,p) -> sprintf "%A - %A" n p.Version)
            |> String.concat (sprintf "%s" System.Environment.NewLine)
            |> sprintf "found packages that were prereleases:%s%s" System.Environment.NewLine
            |> logErrFn
            true

    let runBootstrapper file msg err =
        let logErr args = err(sprintf "%A" args)
        let logMsg args = msg(sprintf "%A" args)
        runexe file logErr logMsg
        |> (=) 0

