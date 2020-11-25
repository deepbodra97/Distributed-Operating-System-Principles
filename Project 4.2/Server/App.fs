module ClientServer.App

open System
open System.Collections.Generic
open WebSharper
open WebSharper.Sitelets

open System.Text.RegularExpressions

/// The types used by this application.
module Model =

    //
    type User = {
        id: int
        username: string
        password: string
    }

    type Subscribe = {
        publisher: string
        subscriber: string
    }

    type Tweet = {
        id: string
        reId: string
        text: string
        tType: string
        by: string
    }

    type QueryTweet = {
        qType: string
        qName: string
        by: string
    }

    /// The type of REST API endpoints.
    /// This defines the set of requests accepted by our API.
    type ApiEndPoint =

        // Accepts POST requests to /usersMap with User as JSON body
        | [<EndPoint "POST /users"; Json "User">]
            CreateUser of User: User

        | [<EndPoint "POST /users/login"; Json "User">]
            LoginUser of User: User

        /// Accepts GET requests to /usersMap
        // | [<EndPoint "GET /usersMap">]
            // GetPeople

        /// Accepts GET requests to /usersMap/{id}
        | [<EndPoint "GET /users">]
            GetUser of username: string
        | [<EndPoint "POST /users/subscribe"; Json "Subscribe">]
            SubscribeTo of Subscribe: Subscribe

        /// Accepts PUT requests to /usersMap with User as JSON body
        // | [<EndPoint "PUT /usersMap"; Json "User">]
            // EditPerson of User: User

        /// Accepts DELETE requests to /usersMap/{id}
        // | [<EndPoint "DELETE /usersMap">]
        //     DeletePerson of id: int
        
        // For Tweets 
        | [<EndPoint "POST /tweets"; Json "Tweet">]
            CreateTweet of Tweet: Tweet
        | [<EndPoint "POST /tweets/search"; Json "QueryTweet">]
            QueryTweet of QueryTweet: QueryTweet

    /// The type of all endpoints for the application.
    type EndPoint =
        // Accepts requests to /
        | [<EndPoint "/">] Home

        // Accepts requests to /api/...
        | [<EndPoint "/api">] Api of Cors<ApiEndPoint>

    /// Error result value.
    type Error = { error : string }

    /// Alias representing the success or failure of an operation.
    /// The Ok case contains a success value to return as JSON.
    /// The Error case contains an HTTP status and a JSON error to return.
    type ApiResult<'T> = Result<'T, Http.Status * Error>

    /// Result value for CreateUser.
    type Id = { id : string }

open Model

module Utils =
    let getRandomString n = // generate random alphnumeric string of length n
        let rnd = System.Random()
        let chars = Array.concat([[|'a' .. 'z'|];[|'A' .. 'Z'|];[|'0' .. '9'|]])
        let sz = Array.length chars in
        System.String(Array.init n (fun _ -> chars.[rnd.Next sz]))
    
    let regexHashTag = "#[A-Za-z0-9]+" // regex for hashtag
    let regexMention = "@[A-Za-z0-9]+" // regex for mention

    let (|Regex|_|) pattern input =
        let matches = Regex.Matches(input, pattern)
        Some([ for m in matches -> m.Value])
    
    let getPatternMatches regex tweet =
        match tweet with
        | Regex regex tags -> tags
        | _ -> []

open Utils

/// This module implements the back-end of the application.
/// It's a CRUD application maintaining a basic in-memory database of usersMap.
module Backend =

    let private usersMap = new Dictionary<string, User>()

    let private tweetsMap = new Dictionary<string, Tweet>() // tweetId -> Tweet
    let private tweetsByUsername = new Dictionary<string, List<string>>() // username -> tweetId
    let private tweetsByHashTag = new Dictionary<string, List<string>>() // hashtag -> tweetId
    let private tweetsByMention = new Dictionary<string, List<string>>() // mention -> tweetId
    let private subscriptionsMap = new Dictionary<string, HashSet<string>>() // subscriptions of a given user
    let private subscribersMap = new Dictionary<string,  List<string>>()

    let maxLenTweetId = 3
    /// The highest id used so far, incremented each time a person is POSTed.
    // let private lastId = ref 0

    let userNotFound() : ApiResult<'T> =
        Error (Http.Status.NotFound, { error = "Please register first" })
    
    let userAlreadyExists() : ApiResult<'T> =
        Error (Http.Status.NotFound, { error = "User already exists" })
    
    let wrongPassword() : ApiResult<'Ts> =
        Error (Http.Status.NotFound, { error = "Seems like you forgot your password or someone hacked your account ;)" })
    
    let userAlreadySubscribed() : ApiResult<'T> =
        Error (Http.Status.NotFound, { error = "User already subscribed" })

    // let GetPeople () : ApiResult<User[]> =
    //     lock usersMap <| fun () ->
    //         usersMap
    //         |> Seq.map (fun (KeyValue(_, person)) -> person)
    //         |> Array.ofSeq
    //         |> Ok

    // Create User
    let CreateUser (data: User) : ApiResult<Id> =
        lock usersMap <| fun () ->
            // incr lastId
            match usersMap.TryGetValue(data.username) with
            | true, _ ->
                printfn "UserCreationError: User already exists"
                userAlreadyExists()
            | false, _ ->
                usersMap.[data.username] <- data
                Ok { id =  data.username}
    
    let LoginUser (data: User) : ApiResult<Id> =
        lock usersMap <| fun () ->
            match usersMap.TryGetValue(data.username) with
            | true, user ->
                if user.password = data.password then
                    Ok { id =  data.username}
                else
                    wrongPassword()
            | false, _ ->
                userNotFound()

    let GetUser (username: string) : ApiResult<User> =
        lock usersMap <| fun () ->
            match usersMap.TryGetValue(username) with
            | true, user -> Ok user
            | false, _ -> userNotFound()
    
    let SubscribeTo (subscribe: Subscribe) : ApiResult<Id> =
        if not (subscriptionsMap.ContainsKey(subscribe.subscriber)) then    
            lock subscriptionsMap <| fun () ->    
                let subscriptions = subscriptionsMap.GetValueOrDefault(subscribe.subscriber, new HashSet<string>())
                subscriptions.Add(subscribe.publisher) |> ignore
                subscriptionsMap.[subscribe.subscriber] <- subscriptions
            lock subscribersMap <| fun () ->
                let subscribers = subscribersMap.GetValueOrDefault(subscribe.publisher, new List<string>())
                subscribers.Add(subscribe.subscriber)
                subscribersMap.[subscribe.publisher] <- subscribers
            Ok {id="200"}
        else userAlreadySubscribed()

    //  For Tweets
    let addTweet (tweet: Tweet) = // add this tweet to the database
        lock tweetsMap <| fun () ->    
            tweetsMap.[tweet.id] <- tweet
        
        lock tweetsByUsername <| fun () ->
            let tweetIds = tweetsByUsername.GetValueOrDefault(tweet.by, new List<string>())
            tweetIds.Add(tweet.id)
            tweetsByUsername.[tweet.by] <- tweetIds

        lock tweetsByHashTag <| fun () -> // add to tweetsByHashTag
            let hashTags = getPatternMatches regexHashTag tweet.text
            for tag in hashTags do
                let tweetIds = tweetsByHashTag.GetValueOrDefault(tag, new List<string>())
                tweetIds.Add(tweet.id)
                tweetsByHashTag.[tag] <- tweetIds

        lock tweetsByMention <| fun () ->
            let mentions = getPatternMatches regexMention tweet.text
            for mention in mentions do
                let tweetIds = tweetsByMention.GetValueOrDefault(mention, new List<string>())
                tweetIds.Add(tweet.id)
                tweetsByMention.[mention] <- tweetIds

    let CreateTweet (data: Tweet) : ApiResult<Id> =
        // incr lastId
        printfn "create tweet called"
        let tweetId = getRandomString maxLenTweetId
        match data.tType with
        | "tweet" -> // if new tweet
            printfn "New Tweet [%s] by [%s]" data.text data.by
            addTweet {data with id=tweetId}
            // pushTweet tweet
        | "retweet" -> // if it is a retweet
            if tweetsMap.ContainsKey(data.reId) then
                let retweet = {data with id=tweetId; text=tweetsMap.Item(data.reId).text}
                printfn "ReTweet [%s] by [%s]" retweet.text retweet.by
                addTweet retweet
        Ok { id =  tweetId}
        // pushTweet retweet # TODO
    
    let SearchTweet query : ApiResult<Tweet[]> =
        printfn "%A %A %A %A" tweetsByUsername tweetsByHashTag tweetsByMention query
        let response = new List<Tweet>()
        match query.qType with
        | "subscription" -> // get tweets from subscription
            for publisher in subscriptionsMap.GetValueOrDefault(query.by, new HashSet<string>()) do
                for tweetId in tweetsByUsername.GetValueOrDefault(publisher, new List<string>()) do
                    response.Add(tweetsMap.[tweetId])
        | "hashtag" -> // get tweets with this hash tag
            let tag = query.qName
            for tweetId in tweetsByHashTag.GetValueOrDefault(tag, new List<string>()) do
                response.Add(tweetsMap.[tweetId])
        | "mention" -> // //  get tweets with this mention
            let mention = query.qName
            for tweetId in tweetsByMention.GetValueOrDefault(mention, new List<string>()) do
                response.Add(tweetsMap.[tweetId])
        response.ToArray () |> Ok
        // pushTweet retweet # TODO
     

    // let EditPerson (data: User) : ApiResult<Id> =
    //     lock usersMap <| fun () ->
    //         match usersMap.TryGetValue(data.id) with
    //         | true, _ ->
    //             usersMap.[data.id] <- data
    //             Ok { id = data.id }
    //         | false, _ -> personNotFound()

    // let DeletePerson (id: int) : ApiResult<Id> =
    //     lock usersMap <| fun () ->
    //         match usersMap.TryGetValue(id) with
    //         | true, _ ->
    //             usersMap.Remove(id) |> ignore
    //             Ok { id = id }
    //         | false, _ -> personNotFound()

    // On application startup, pre-fill the database with a few usersMap.
    // do List.iter (CreateUser >> ignore) [
    //     { id = 0
    //       username = "Alonzo"
    //       password = "Church"
    //     }
    //     { id = 0
    //       username = "Alan"
    //       password = "Turing"
    //     }
    //     { id = 0
    //       username = "Bertrand"
    //       password = "Russell"
    //     }
    //     { id = 0
    //       username = "Noam"
    //       password = "Chomsky"
    //     }
    // ]

/// The server side website, tying everything together.
module Site =
    open WebSharper.UI
    open WebSharper.UI.Html
    open WebSharper.UI.Server

    /// Helper function to convert our internal ApiResult type into WebSharper Content.
    let JsonContent (result: ApiResult<'T>) : Async<Content<EndPoint>> =
        match result with
        | Ok value ->
            Content.Json value
        | Error (status, error) ->
            Content.Json error
            |> Content.SetStatus status
        |> Content.WithContentType "application/json"

    /// Respond to an ApiEndPoint by calling the corresponding backend function
    /// and converting the result into Content.
    let ApiContent (ep: ApiEndPoint) : Async<Content<EndPoint>> =
        match ep with
        // | GetPeople ->
            // JsonContent (Backend.GetPeople ())
        // | GetUser username ->
            // JsonContent (Backend.GetUser username)
        | CreateUser user ->
            JsonContent (Backend.CreateUser user)
        | LoginUser user ->
            JsonContent (Backend.LoginUser user)
        | SubscribeTo subscribe ->
            JsonContent (Backend.SubscribeTo subscribe)
        // | EditPerson User ->
            // JsonContent (Backend.EditPerson User)
        // | DeletePerson id ->
            // JsonContent (Backend.DeletePerson id)
        | CreateTweet tweet ->
            JsonContent (Backend.CreateTweet tweet)
        | QueryTweet query ->
            JsonContent (Backend.SearchTweet query)

    // A simple HTML home page.
    let HomePage (ctx: Context<EndPoint>) : Async<Content<EndPoint>> =
        // Type-safely creates the URI: "/api/usersMap/1"
        let user1Link = ctx.Link (Api (Cors.Of (GetUser "Alonzo")))
        Content.Page(
            Body = [
                p [] [text "API is running."]
                p [] [
                    text "Try querying: "
                    a [attr.href user1Link] [text user1Link]
                ]
            ]
        )

    /// The Sitelet parses requests into EndPoint values
    /// and dispatches them to the content function.
    let Main corsAllowedOrigins : Sitelet<EndPoint> =
        Application.MultiPage (fun ctx endpoint ->
            match endpoint with
            | Home -> HomePage ctx
            | Api api ->
                Content.Cors api (fun allows ->
                    { allows with
                        Origins = corsAllowedOrigins
                        Headers = ["Content-Type"]
                    }
                ) ApiContent
        )