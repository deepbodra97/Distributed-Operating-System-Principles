#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"

open Akka.Actor
open Akka.FSharp
open Akka.Configuration
open Akka.Serialization
open System.Diagnostics

open System.Text.RegularExpressions

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            actor{
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                serializers {
                    hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                }
                serialization-bindings {
                    ""System.Object"" = hyperion
                }
            }
            remote.helios.tcp {
                hostname = 127.0.0.1
                port = 9001
            }
        }")

type User = {
    id: string
    username: string
    password: string
}

type Tweet = {
    id: string
    text: string
    by: User
}

// different message types
type Message =
    | Start of string // parent starts spawning nodes. Nodes start joining
    | Register of User
    | Tweet of Tweet
    // | StartRequestPhase // Nodes start making 1 request per second
    // | Join of string // route the Join packet
    // | JoinSuccess // parent know that a node has finished joining
    // | NewRow of int * string[] //  (row number, row of routing table)
    // | NewLeaves of Set<string> // leaf set
    // | Route of string * string * int // route the Route(request) packet
    // | RouteSuccess of int // report number of hops to parent
    // | RequestTick // tick every 1 second

// main program
let main () =
    let system = ActorSystem.Create("twitter", configuration) // create an actor system

    let regexHashTag = "#[A-Za-z0-9]+"
    let regexMention = "@[A-Za-z0-9]+"

    let (|Regex|_|) pattern input =
        let matches = Regex.Matches(input, pattern)
        Some([ for m in matches -> m.Value.[1..]])
    
    let getPatternMatches regex tweet =
        match tweet with
        | Regex regex tags -> tags
        | _ -> []

    let server =
        spawn system "server"
            <| fun mailbox ->
                let id = mailbox.Self.Path.Name // id

                let mutable usersMap = Map.empty
                let mutable tweetsByUsername = Map.empty
                let mutable tweetsByHashTag = Map.empty
                let mutable tweetsByMention = Map.empty

                let rec loop() =
                    actor {   
                        let! (msg: Message) = mailbox.Receive() // fetch the message from the queue
                        let sender = mailbox.Sender()
                        match msg with
                        | Register user ->
                            usersMap <- usersMap.Add(user.username, user)
                            printfn "User %A registered" user
                        | Tweet tweet ->
                            tweetsByUsername <- tweetsByUsername.Add(tweet.by.username, tweet)

                            let hashTags = getPatternMatches regexHashTag tweet.text
                            let mentions = getPatternMatches regexMention tweet.text

                            for tag in hashTags do
                                tweetsByHashTag <- tweetsByHashTag.Add(tag, tweet.id)
                            
                            for mention in mentions do
                                tweetsByMention <- tweetsByMention.Add(mention, tweet.id)

                            printfn "%A %A %A" tweetsByUsername tweetsByHashTag tweetsByMention

                        | _ -> return ()
                        return! loop()
                    }
                loop()


    async {
        // let! response = server <? ()
        System.Console.ReadLine() |> ignore
        printfn ""
    } |> Async.RunSynchronously


main ()