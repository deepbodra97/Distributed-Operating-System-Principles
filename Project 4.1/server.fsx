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

type Query = {
    qType: string
    qName: string
    by: User
}

// different message types
type Message =
    | Start of string // parent starts spawning nodes. Nodes start joining
    | Register of User
    | Tweet of Tweet
    | Query of Query
    | QueryResponse of Tweet array
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
        Some([ for m in matches -> m.Value])
    
    let getPatternMatches regex tweet =
        match tweet with
        | Regex regex tags -> tags
        | _ -> []

    let server =
        spawn system "server"
            <| fun mailbox ->
                let id = mailbox.Self.Path.Name // id

                let mutable usersMap: Map<string, User> = Map.empty
                let mutable tweetsMap: Map<string, Tweet> = Map.empty
                let mutable tweetsByUsername: Map<string, string array> = Map.empty
                let mutable tweetsByHashTag: Map<string, string array> = Map.empty
                let mutable tweetsByMention: Map<string, string array> = Map.empty

                let rec loop() =
                    actor {   
                        let! (msg: Message) = mailbox.Receive() // fetch the message from the queue
                        let sender = mailbox.Sender()
                        match msg with
                        | Register user ->
                            usersMap <- usersMap.Add(user.username, user)
                            printfn "User %A registered" user
                        | Tweet tweet ->
                            
                            tweetsMap <- tweetsMap.Add(tweet.id, tweet)

                            let mutable tweetIds =
                                if tweetsByUsername.ContainsKey(tweet.by.username) then tweetsByUsername.Item(tweet.by.username)
                                else [||]
                            tweetIds <- Array.append tweetIds [|tweet.id|]
                            tweetsByUsername <- tweetsByUsername.Add(tweet.by.username, tweetIds)

                            let hashTags = getPatternMatches regexHashTag tweet.text
                            let mentions = getPatternMatches regexMention tweet.text
                            
                            for tag in hashTags do
                                let mutable tweetIds =
                                    if tweetsByHashTag.ContainsKey(tag) then tweetsByHashTag.Item(tag)
                                    else [||]
                                tweetIds <- Array.append tweetIds [|tweet.id|]
                                tweetsByHashTag <- tweetsByHashTag.Add(tag, tweetIds)
                            
                            for mention in mentions do
                                let mutable tweetIds =
                                    if tweetsByMention.ContainsKey(tweet.id) then tweetsByMention.Item(tweet.id)
                                    else [||]
                                tweetIds <- Array.append tweetIds [|tweet.id|]
                                tweetsByMention <- tweetsByMention.Add(mention, tweetIds)

                            printfn "%A %A %A" tweetsByUsername tweetsByHashTag tweetsByMention
                            printfn "New Tweet %A" tweet
                        | Query query ->
                            let mutable response = Array.empty
                            match query.qType with
                            // | "subscription" ->
                                
                            | "hashtag" ->
                                for tweetId in tweetsByHashTag.Item(query.qName) do
                                    response <- Array.append response [|tweetsMap.Item(tweetId)|]
                            // | "mention" ->
                            sender <! QueryResponse response
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