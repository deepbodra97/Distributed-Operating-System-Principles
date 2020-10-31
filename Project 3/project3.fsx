#r "nuget: Akka.FSharp"

open Akka.Actor
open Akka.FSharp
open System
open System.Diagnostics

// different message types
type Message =
    | Start of string
    | StartRequestPhase
    | Join of string
    | JoinSuccess
    | NewRow of int * string[]
    | NewLeaves of Set<string>
    | Route of string * string * int
    | RouteSuccess of int
    | RequestTick

// main program
let main numNodes numRequests =
    use system = ActorSystem.Create("Project3") // create an actor systems
    let getRandomInt start stop =  // get random integer [start, stop]
        let rnd = System.Random()
        rnd.Next(start, stop+1)

    let decimalTo (x: int) (b: int) =
        System.Convert.ToString(x, b).PadLeft(8, '0')
    let toDecimal (x: string) =
        System.Convert.ToInt32(x, 16)    
    
    let hexToDecimalMap = Map.empty.Add('0', 0).Add('1', 1).Add('2', 2).Add('3', 3).Add('4', 4).Add('5', 5).Add('6', 6).Add('7', 7).Add('8', 8).Add('9', 9).Add('a', 10).Add('b', 11).Add('c', 12).Add('d', 13).Add('e', 14).Add('f', 15)
    let hexToDecimal (c:char) =
        hexToDecimalMap.[c]
    
    let longestCommonPrefix (s1:string) (s2:string) =
        let n1 = s1.Length
        let n2 = s2.Length
        let rec loop i j =
            if i<n1 && j<n2 then
                if s1.[i] = s2.[j] then loop (i+1) (j+1)
                else i-1
            else i-1
        let index = loop 0 0
        if index = -1 then ""
        else s1.[0..index]

    let findClosest id (set:Set<string>) =
        set
        |> Seq.mapi (fun i v -> v, abs(toDecimal(v)-toDecimal(id)))
        |> Seq.minBy snd
        |> fst

    let child (childMailbox: Actor<_>) = // nodes participating in gossip or pushsum
        let id = childMailbox.Self.Path.Name // id
        let mutable cancelable = Unchecked.defaultof<ICancelable>

        let routingTable = Array2D.create 16 16 ""
        let mutable largerLeaves = Set.empty
        let mutable smallerLeaves = Set.empty

        let mutable nRequests = 0

        let updateSmallerLeaves newNode =
            if smallerLeaves.IsEmpty || smallerLeaves.Count < 8 then
                smallerLeaves <- smallerLeaves.Add newNode
            elif smallerLeaves.Count = 8 then
                if newNode > smallerLeaves.MinimumElement then
                    smallerLeaves <- smallerLeaves.Remove smallerLeaves.MinimumElement
        
        let updateLargerLeaves newNode =
            if largerLeaves.IsEmpty || largerLeaves.Count < 8 then
                largerLeaves <- largerLeaves.Add newNode
            elif largerLeaves.Count = 8 then
                if newNode < largerLeaves.MaximumElement then
                    largerLeaves <- largerLeaves.Remove largerLeaves.MaximumElement

        let sendLeaves destination =
            let leaves = Set.union smallerLeaves largerLeaves
            system.ActorSelection("/user/parent/"+destination) <! NewLeaves (leaves)
        
        let getRowCol destination =
            let commonPrefix = longestCommonPrefix id destination
            let row = String.length commonPrefix
            if String.length(commonPrefix) < destination.Length then
                let col =  destination.[String.length(commonPrefix)] |> hexToDecimal
                row, col
            else
                row, 16

        let lookupRoutingTable destination =
            let (row, col) = getRowCol destination
            routingTable.[row, col]

        let sendMatchingRows destination =
            let (row, _) = getRowCol destination
            for i in 0 .. row do
                system.ActorSelection("/user/parent/"+destination) <! NewRow (row, routingTable.[row, *].Clone() :?> string[])

        let forward source destination nHops =
            let nextNode = lookupRoutingTable destination
            if nextNode = "" then
                if destination < id && smallerLeaves.Count > 0 then
                    printfn "No entry in routing table. Using smaller leaves"
                    system.ActorSelection("/user/parent/"+smallerLeaves.MinimumElement) <! Route (source, destination, nHops+1)
                elif destination > id && largerLeaves.Count > 0 then
                    printfn "No entry in routing table. Using larger leaves"
                    system.ActorSelection("/user/parent/"+largerLeaves.MaximumElement) <! Route (source, destination, nHops+1)
                else
                    printfn "Node %s cannont route request for %s %A %A" id destination smallerLeaves largerLeaves
                    system.ActorSelection("/user/parent") <! RouteSuccess (nHops+1)
            else
                system.ActorSelection("/user/parent/"+nextNode) <! Route (source, destination, nHops+1)

        let rec childLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | Start start ->
                    // printfn "Child %s's coordinator is %s" id start
                    if start <> "" then
                        system.ActorSelection("/user/parent/"+start) <! Join id
                    else
                        system.ActorSelection("/user/parent") <! JoinSuccess
                | StartRequestPhase ->
                    // printfn "StartRequestPhase %s" id
                    // printfn "%s %A %A" id smallerLeaves largerLeaves
                    // printfn "%s %A" id routingTable
                    cancelable <- system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(1000.0)), childMailbox.Self, (RequestTick), childMailbox.Self)
                | Join newNode ->
                    if id = newNode then
                        printfn "%s got its join message back" id
                        system.ActorSelection("/user/parent") <! JoinSuccess
                    else
                        printfn "%s joined" newNode
                        if (lookupRoutingTable newNode) = "" then
                            let (row, col) = getRowCol newNode
                            routingTable.[row, col] <- newNode
                        // printfn "%d %d" row col
                        // let mutable nextNode = "" 
                        if newNode < id then
                            updateSmallerLeaves newNode
                            system.ActorSelection("/user/parent/"+(findClosest id smallerLeaves)) <! Join newNode
                        elif newNode > id then
                            updateLargerLeaves newNode
                            system.ActorSelection("/user/parent/"+(findClosest id largerLeaves)) <! Join newNode
                        sendMatchingRows newNode
                        sendLeaves newNode
                        // printfn "%s %A %A" id smallerLeaves largerLeaves
                        // printfn "%s %A" id routingTable
                | NewRow (row_num, row) ->
                    printfn "NewRow %A" row
                    for i in 0 .. row.Length-1 do
                        if routingTable.[row_num, i] = "" then
                            routingTable.[row_num, i] <- row.[i]
                | NewLeaves newLeaves ->
                    for leaf in newLeaves do
                        if leaf <> id then
                            if leaf < id then updateSmallerLeaves leaf
                            else updateLargerLeaves leaf
                | RequestTick ->
                    printfn "RequestTick %s" id
                    nRequests <- nRequests + 1
                    system.ActorSelection("/user/parent/"+id) <! Route (id, decimalTo (getRandomInt 1 numNodes) 16, -1)
                    // system.ActorSelection("/user/parent/"+id) <! Route (id, (decimalTo 1 16), -1)
                    if nRequests = numRequests then
                        cancelable.Cancel ()
                | Route (source, destination, nHops) ->
                    printfn "Child %s received route" id
                    if id = destination then
                        printfn "%s consumed a message" id
                        system.ActorSelection("/user/parent") <! RouteSuccess (nHops+1)
                    else
                        if destination < id then
                            if smallerLeaves.Count > 0 then
                                if abs(toDecimal(id)-toDecimal(destination)) < abs(toDecimal(findClosest destination smallerLeaves)-toDecimal(destination)) then
                                    printfn "Nearest %s consumed a message" id
                                    system.ActorSelection("/user/parent") <! RouteSuccess (nHops+1)
                                elif destination >= smallerLeaves.MinimumElement then
                                    system.ActorSelection("/user/parent/"+(findClosest id smallerLeaves)) <! Route (source, destination, nHops+1)
                                else
                                    forward source destination nHops
                            else
                                forward source destination nHops
                        if destination > id then
                            if largerLeaves.Count > 0 then
                                if abs(toDecimal(id)-toDecimal(destination)) < abs(toDecimal(findClosest destination largerLeaves)-toDecimal(destination)) then
                                    printfn "%s consumed a message" id
                                    system.ActorSelection("/user/parent") <! RouteSuccess (nHops+1)
                                elif destination <= largerLeaves.MaximumElement then
                                    system.ActorSelection("/user/parent/"+(findClosest id largerLeaves)) <! Route (source, destination, nHops+1)
                                else
                                    forward source destination nHops
                            else
                                forward source destination nHops
                | _ -> return ()
                return! childLoop()
            }

        childLoop()

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let mutable mainSender = Unchecked.defaultof<IActorRef>

                let activeNodes = Array.create numNodes ""
                let mutable nSuccessfulJoins = 0
                let mutable nSuccessfulRequests = 0
                let mutable nHops = 0

                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        let sender = parentMailbox.Sender()
                        match msg with
                        | Start start ->
                            printfn "Parent received start"
                            mainSender <- sender
                            for i in 1 .. numNodes do
                                let id = decimalTo (getRandomInt 1 1000000) 16
                                activeNodes.[i-1] <- id
                                let childRef = spawn parentMailbox id child
                                if i = 1 then
                                    childRef <! Start ""
                                else
                                    let poc = activeNodes.[getRandomInt 0 (i-2)] 
                                    childRef <! Start poc
                        | JoinSuccess ->
                            nSuccessfulJoins <- nSuccessfulJoins + 1
                            if nSuccessfulJoins = numNodes then
                                printfn "Everyone has joined"
                                system.ActorSelection("/user/parent/*") <! StartRequestPhase
                        | RouteSuccess hops->
                            printfn "RouteSuccess %d" hops
                            nSuccessfulRequests <- nSuccessfulRequests + 1
                            nHops <- nHops + hops
                            if nSuccessfulRequests = (numRequests * numNodes) then
                                printfn "Average hops per requests = %f" (float(nHops)/(float(numRequests)*float(numNodes)))
                                system.Terminate() |> ignore
                                mainSender <! "Done"
                        | _ -> return ()
                        return! parentLoop()
                    }
                parentLoop()

            // default supervisor strategy
            <| [ SpawnOption.SupervisorStrategy (
                    Strategy.OneForOne(fun e ->
                    match e with 
                    | _ -> SupervisorStrategy.DefaultDecider.Decide(e)))]


    async {
        // let timer = new Stopwatch()
        // timer.Start()
        let! response = parent <? Start "0"
        // printfn "Convergence Time: %s ms" <| timer.ElapsedMilliseconds.ToString()
        printfn "Exit"
    } |> Async.RunSynchronously

// let args : string array = fsi.CommandLineArgs |> Array.tail
// let n = int args.[0]
// let topology = args.[1]
// let algorithm = args.[2]
main 100 1