﻿module ProjectInterpreter
open ProjectParser
open AudioGen

(* Environment to store variables *)
type Env = Map<string, TuneBuilder>

(* Used to fail with error and exit *)
let fail msg = 
    printfn "\n%s" msg
    printfn "\n-- END ARPEGGIFY INTERPRETER --"
    exit 1

// Calculate amount to modify note by
let evalSymbol s = 
    match s with 
    | Sharp -> 1
    | Flat -> -1

// return numeric representation of note, notes represented by their distance in half steps from C = 261.64 HZ
let evalNote n = 
    match n with
    | C -> 0
    | D -> 2
    | E -> 4
    | F -> 5
    | G -> 7
    | A -> 9
    | B -> 11   

// return number representing root
let rec evalRoot r = 
    match r with 
    | Note n -> evalNote n
    | Accidental (n,s) -> evalRoot n + evalSymbol s

// eval extension - return array of notes within a given chord
let evalExt e = 
    match e with
    | Major7 -> [4; 7; 11]
    | Minor7 -> [3; 7; 10]
    | Dom7 -> [4; 7; 10]

// Return list of note arrays, each one corresponding to the notes within a specified chord
let rec evalChord cs = 
    match cs with
    | head :: tail ->
        let (r,e) = head
        let root = evalRoot r
        let notesInChord = List.toArray(root :: (List.map (fun x -> x + root)(evalExt e)))
        notesInChord :: evalChord tail
    | _ -> []
    
// Return an (int[] * Rhythm) list, matching each chord's notes with that chord's length
let rec evalPhrase (ps: Phrase list) = 
    match ps with
    | head :: tail ->
        match head with
        | PhraseLit (cs, rs) -> 
            let notes = evalChord cs
            List.append (List.zip notes rs) (evalPhrase tail) 
        | _ -> fail "Error: phrase list cannot be evaluated correctly"              
    | _ -> []

// Evaluate an assignment operation, retrieving stored info from environment if necessary,
// returns tuple of return value and new environment
let evalAssignment typeName varName (tb: TuneBuilder) env = 
    match typeName with
    | TVar ->
        let t = tb
        match t with
        | Tune t -> 
            match t with
            | TuneLit ps -> 
                let findPhrase p : Phrase = 
                    match p with
                    | PhraseLit _ -> p
                    | PhraseVar (c,r) -> 
                        // replace with function
                        match Map.tryFind c env with
                        | Some chords ->
                            match chords with
                            | Chords chords -> 
                                match Map.tryFind r env with
                                | Some rhythms ->
                                    match rhythms with
                                    | Rhythms rhythms -> 
                                        if List.length chords = List.length rhythms then 
                                            (chords,rhythms) |> PhraseLit
                                        else fail "Error: Attempting to assign a different length chords and rhythms to a phrase"
                                    | _ -> fail "Error: variabe not of type Rhythms"
                                | None -> sprintf "Error: variable %s not defined" r |> fail
                            | _ -> fail "Error: variable not of type Chords"
                        | None -> sprintf "Error: variable %s not yet defined" c |> fail

                let tune = ps |> List.map findPhrase |> TuneLit |> Tune
                tune, Map.add varName tune env

            | TuneVar vs -> 
                let retrieve p : Phrase = 
                    match Map.tryFind p env with
                    | Some phrase -> 
                        match phrase with
                        | Phrase phrase -> phrase
                        | _ -> sprintf "Error: variable %s not of type Tune" varName |> fail
                    | None -> sprintf  "Error: variable %s is not defined" varName |> fail

                let tune = vs |> List.map retrieve |> TuneLit |> Tune
                tune, Map.add varName tune env
        | _ -> fail "Type mismatch: Attempting to assign something other than a tune to a Tune variable"
    | PVar ->
        let p = tb     
        match p with
        | Phrase ph ->
            match ph with
            | PhraseLit(cs, rs) -> 
                if List.length cs = List.length rs then
                    tb, Map.add varName tb env
                else sprintf "Error: Attempting to add different length chords and rhythms to phrase %s" varName |> fail
            | PhraseVar (c, r) ->
                match Map.tryFind c env with
                | Some chords ->
                    match chords with
                    | Chords chords -> 
                        match Map.tryFind r env with
                        | Some rhythms ->
                            match rhythms with
                            | Rhythms rhythms -> 
                                if List.length chords = List.length rhythms then 
                                    let p = (chords,rhythms) |> PhraseLit |> Phrase
                                    p, Map.add varName p env
                                else fail "Error: Attempting to assign a different length chords and rhythms to a phrase"
                            | _ -> fail "Error: variabe not of type Rhythms"
                        | None -> sprintf "Error: variable %s not defined" r |> fail
                    | _ -> fail "Error: variable not of type Chords"
                | None -> sprintf "Error: variable %s not yet defined" c |> fail
        | _ -> fail "Type mismatch: attempting to assign something other than a phrase to variable"
    | CVar -> 
        let chords = tb
        match chords with
        | Chords chords -> tb, Map.add varName tb env
        | _ -> fail "Error: atttempting to assign something other than chords to a Chords variable"
    | RVar ->
        let r = tb
        match r with
        | Rhythms r -> tb, Map.add varName tb env
        | _ -> fail "Error: attempting to assign something other than rhythms to a Rhythms variable"

// Choose appropriate notes to create arpeggios, AKA, where the magic happens
let rec arpeggiate (evaled: (int [] * Rhythm) list) = 
    match evaled with 
    | head :: tail -> 
        match head with
            | (notes, number) -> 
                let list = [0 .. number-1] // list represents each note played to satisfy rhythm
                List.append (List.map(fun x -> notes.[x % 4]) list) (arpeggiate tail)
    | _ -> []


// Evaluate expressions, returning a return value and a new environment
let rec evalExpr (e: Expr) (env: Map<string, TuneBuilder>) = 
    match e with
    | Assignment ((typeName,varName), tb) -> evalAssignment typeName varName tb env
    | Seq (e1, e2) ->
        let _, env' = evalExpr e1 env
        evalExpr e2 env'              

(* Evaluate an arpeggify program, first evaluate all assignments in sequence,
    then generate audio from the tune in the program's last line *)
let eval e env output = 
   let (tune, _) = evalExpr e env // store return value of last line of program

   match tune with
   | Tune tune -> 
       match tune with
       | TuneLit tune ->
            generateAudio (arpeggiate (evalPhrase tune)) output
            0 // Return 0 to indicate success
        | _ -> fail "Evaluation error, tune interpreted incorrectly"                                                                                                                                                   
   | _ -> fail "Error: Last line of program must be a tune assignment"