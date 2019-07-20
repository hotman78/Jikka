module Jikka.Optimization

open Jikka.Semantics

let embed : list<ValName * Schema<RType>> =
    [ (ValName "+", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, ZahlRTy))))
      (ValName "-", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, ZahlRTy))))
      (ValName "*", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, ZahlRTy))))
      (ValName "/", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, ZahlRTy))))
      (ValName "%", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, ZahlRTy))))
      (ValName "**", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, NatRTy))))
      (ValName "negate", Monotype(FunRTy(ZahlRTy, ZahlRTy)))
      (ValName "max2", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, ZahlRTy))))
      (ValName "min2", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, ZahlRTy))))
      (ValName "!", Monotype(FunRTy(BoolRTy, BoolRTy)))
      (ValName "&&", Monotype(FunRTy(BoolRTy, FunRTy(BoolRTy, BoolRTy))))
      (ValName "||", Monotype(FunRTy(BoolRTy, FunRTy(BoolRTy, BoolRTy))))
      (ValName "<=", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, BoolRTy))))
      (ValName "<", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, BoolRTy))))
      (ValName "=", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, BoolRTy))))
      (ValName "<>", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, BoolRTy))))
      (ValName ">=", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, BoolRTy))))
      (ValName ">", Monotype(FunRTy(ZahlRTy, FunRTy(ZahlRTy, BoolRTy))))
      (ValName "count", Monotype(FunRTy(NatRTy, FunRTy(FunRTy(ZahlRTy, BoolRTy), NatRTy))))
      (ValName "sum", Monotype(FunRTy(NatRTy, FunRTy(FunRTy(ZahlRTy, ZahlRTy), ZahlRTy))))
      (ValName "max", Monotype(FunRTy(NatRTy, FunRTy(FunRTy(ZahlRTy, ZahlRTy), ZahlRTy))))
      (ValName "min", Monotype(FunRTy(NatRTy, FunRTy(FunRTy(ZahlRTy, ZahlRTy), ZahlRTy))))
      (ValName "count3", Monotype(FunRTy(NatRTy, FunRTy(NatRTy, FunRTy(FunRTy(ZahlRTy, BoolRTy), NatRTy)))))
      (ValName "sum3", Monotype(FunRTy(NatRTy, FunRTy(NatRTy, FunRTy(FunRTy(ZahlRTy, ZahlRTy), ZahlRTy)))))
      (ValName "max3", Monotype(FunRTy(NatRTy, FunRTy(NatRTy, FunRTy(FunRTy(ZahlRTy, ZahlRTy), ZahlRTy)))))
      (ValName "min3", Monotype(FunRTy(NatRTy, FunRTy(NatRTy, FunRTy(FunRTy(ZahlRTy, ZahlRTy), ZahlRTy)))))
      (ValName "boolToZahl", Monotype(FunRTy(BoolRTy, ZahlRTy)))
      (ValName "zahlToBool", Monotype(FunRTy(ZahlRTy, BoolRTy))) ]

let foldBoundVar (f : 'a -> int -> 'a) : 'a -> Expr -> 'a =
    let rec go depth acc =
        function
        | VarExp i -> f acc (i - depth)
        | FreeVarExp _ -> acc
        | LamExp(_, e) -> go (depth + 1) acc e
        | AppExp(e1, e2) -> go depth (go depth acc e1) e2
        | IfThenElseExp(e1, e2, e3) -> go depth (go depth (go depth acc e1) e2) e3
        | IntExp _ -> acc
        | BoolExp _ -> acc
    go 0

let mapBoundVar (f : int -> int -> Expr) : Expr -> Expr =
    let rec go depth e =
        match e with
        | VarExp i -> f depth i
        | FreeVarExp _ -> e
        | LamExp(t, e1) -> LamExp(t, go (depth + 1) e1)
        | AppExp(e1, e2) -> AppExp(go depth e1, go depth e2)
        | IfThenElseExp(e1, e2, e3) -> IfThenElseExp(go depth e1, go depth e2, go depth e3)
        | IntExp _ -> e
        | BoolExp _ -> e
    go 0

let findBoundVar (n : int) : Expr -> bool = foldBoundVar (fun acc i -> acc || i = n) false

let unshiftLambda : Expr -> Expr =
    mapBoundVar (fun depth i ->
        VarExp(if i > depth then i - 1
               elif i < depth then i
               else failwith "failed to shift"))

let assignLambda (n : int) (e : Expr) : Expr -> Expr =
    mapBoundVar (fun depth i ->
        if i = n + depth then e
        else VarExp i)

let optimize (typeenv : list<ValName * Schema<RType>>) (gensym : unit -> TyName) : Expr -> Expr =
    let app f x = AppExp(f, x)
    let app2 f x y = AppExp(AppExp(f, x), y)
    let app3 f x y z = AppExp(AppExp(AppExp(f, x), y), z)

    let fvarTuple (name : string) : ValName * RType =
        match List.tryFind (fun (x, _) -> x = ValName name) typeenv with
        | None -> failwithf "undefined name: %s" name
        | Some(x, scm) -> (x, realizeSchema gensym scm)

    let fvar (name : string) : Expr = FreeVarExp(fvarTuple name)
    let appf name x = AppExp(fvar name, x)
    let app2f name x y = AppExp(AppExp(fvar name, x), y)
    let app3f name x y z = AppExp(AppExp(AppExp(fvar name, x), y), z)
    let add : Expr -> Expr -> Expr = app2 (fvar "+")
    let sub : Expr -> Expr -> Expr = app2 (fvar "-")
    let mul : Expr -> Expr -> Expr = app2 (fvar "*")
    let addOrSub name = (name = "+" || name = "-")
    let maxOrMin name = (name = "max3" || name = "min3")

    let rec go (acc : list<RType>) : Expr -> (Expr * RType) =
        function
        | VarExp i -> (VarExp i, acc.[i])
        | FreeVarExp(ValName x, _) ->
            let (x, t) = fvarTuple x
            (FreeVarExp(x, t), t)
        | LamExp(t1, e) ->
            let (e, t2) = go (t1 :: acc) e
            (LamExp(t1, e), FunRTy(t1, t2))
        | AppExp(f, x) ->
            let (f, t) = go acc f

            let go e =
                let (e, _) = go acc e in e

            let y =
                match AppExp(f, x) with
                | AppExp(FreeVarExp(ValName f, t), x) ->
                    let x = go x
                    match (f, x) with
                    | ("negate", IntExp n) -> IntExp(-n)
                    | ("negate", AppExp(FreeVarExp(ValName "negate", _), e)) -> e
                    | ("!", BoolExp p) -> BoolExp(not p)
                    | ("!", AppExp(FreeVarExp(ValName "!", _), e)) -> e
                    | _ -> AppExp(FreeVarExp(ValName f, t), x)
                | AppExp(AppExp(FreeVarExp(ValName f, t), x), y) ->
                    let x = go x
                    let y = go y
                    match (f, x, y) with
                    | ("+", IntExp a, IntExp b) -> IntExp(a + b)
                    | ("-", IntExp a, IntExp b) -> IntExp(a - b)
                    | ("*", IntExp a, IntExp b) -> IntExp(a * b)
                    | ("/", IntExp a, IntExp b) -> IntExp(a / b)
                    | ("%", IntExp a, IntExp b) -> IntExp(a % b)
                    | ("**", IntExp a, IntExp b) -> IntExp(a ** int (b))
                    | ("+", e, IntExp n) when n = 0I -> e
                    | ("-", e, IntExp n) when n = 0I -> e
                    | ("*", e, IntExp n) when n = 1I -> e
                    | ("*", _, IntExp n) when n = 0I -> IntExp 0I
                    | ("*", e, IntExp n) when n = -1I -> go (app (fvar "negate") e)
                    | ("/", e, IntExp n) when n = 1I -> e
                    | ("/", e, IntExp n) when n = -1I -> go (app (fvar "negate") e)
                    | ("%", _, IntExp n) when n = 1I -> IntExp 0I
                    | ("**", e, IntExp n) when n = 1I -> e
                    | ("+", IntExp n, e) when n = 0I -> e
                    | ("-", IntExp n, e) when n = 0I -> go (app (fvar "negate") e)
                    | ("*", IntExp n, e) when n = 1I -> e
                    | ("*", IntExp n, _) when n = 0I -> IntExp 0I
                    | ("*", IntExp n, e) when n = -1I -> go (app (fvar "negate") e)
                    | ("/", IntExp n, _) when n = 0I -> IntExp 0I
                    | ("/", IntExp n, e) when n = -1I -> go (app (fvar "negate") e)
                    | ("%", IntExp n, _) when n = 0I -> IntExp 0I
                    | ("**", IntExp n, _) when n = 1I -> IntExp 1I
                    | ("&&", BoolExp a, BoolExp b) -> BoolExp(a && b)
                    | ("||", BoolExp a, BoolExp b) -> BoolExp(a || b)
                    | ("&&", BoolExp true, e) -> e
                    | ("&&", BoolExp false, _) -> BoolExp false
                    | ("||", BoolExp true, _) -> BoolExp true
                    | ("||", BoolExp false, e) -> e
                    | ("&&", e, BoolExp true) -> e
                    | ("&&", _, BoolExp false) -> BoolExp false
                    | ("||", _, BoolExp true) -> BoolExp true
                    | ("||", e, BoolExp false) -> e
                    | ("<", IntExp a, IntExp b) -> BoolExp(a < b)
                    | ("<=", IntExp a, IntExp b) -> BoolExp(a <= b)
                    | ("=", IntExp a, IntExp b) -> BoolExp(a = b)
                    | ("<>", IntExp a, IntExp b) -> BoolExp(a <> b)
                    | (">=", IntExp a, IntExp b) -> BoolExp(a >= b)
                    | (">", IntExp a, IntExp b) -> BoolExp(a > b)
                    | ("=", e, IntExp n) when n = 0I -> go (appf "!" (appf "zahlToBool" e))
                    | ("=", IntExp n, e) when n = 0I -> go (appf "!" (appf "zahlToBool" e))
                    | ("<>", e, IntExp n) when n = 0I -> go (appf "zahlToBool" e)
                    | ("<>", IntExp n, e) when n = 0I -> go (appf "zahlToBool" e)
                    | ("count", n, f) -> go (app3f "count3" (IntExp 0I) n f)
                    | ("sum", n, f) -> go (app3f "sum3" (IntExp 0I) n f)
                    | ("max", n, f) -> go (app3f "max3" (IntExp 0I) n f)
                    | ("min", n, f) -> go (app3f "min3" (IntExp 0I) n f)
                    | _ -> AppExp(AppExp(FreeVarExp(ValName f, t), x), y)
                | AppExp(AppExp(AppExp(FreeVarExp(ValName f, t), x), y), z) ->
                    let x = go x
                    let y = go y
                    let z = go z
                    match (f, x, y, z) with
                    | ("count3", IntExp l, IntExp r, _) when l = r -> IntExp 0I
                    | ("count3", l, r, LamExp(_, e)) when not (findBoundVar 0 e) -> go (mul (sub r l) (appf "boolToZahl" (unshiftLambda e)))
                    | ("sum3", IntExp l, IntExp r, _) when l = r -> IntExp 0I
                    | ("sum3", l, r, LamExp(_, e)) when not (findBoundVar 0 e) -> go (mul (sub r l) (unshiftLambda e))
                    | ("sum3", l, r, LamExp(t, AppExp(AppExp(FreeVarExp(ValName op, _), e1), e2))) when addOrSub op ->
                        let e1 = app3f "sum3" l r (LamExp(t, e1))
                        let e2 = app3f "sum3" l r (LamExp(t, e2))
                        go (app2f op e1 e2)
                    | ("sum3", l, r, LamExp(t, AppExp(AppExp(FreeVarExp(ValName "*", _), e1), e2))) when not (findBoundVar 0 e1) ->
                        let e1 = unshiftLambda e1
                        let e2 = app3f "sum3" l r (LamExp(t, e2))
                        go (mul e1 e2)
                    | ("sum3", l, r, LamExp(t, AppExp(AppExp(FreeVarExp(ValName "*", _), e1), e2))) when not (findBoundVar 0 e2) ->
                        let e1 = app3f "sum3" l r (LamExp(t, e1))
                        let e2 = unshiftLambda e2
                        go (mul e1 e2)
                    | (maxlike, _, _, LamExp(t, e)) when maxOrMin maxlike && not (findBoundVar 0 e) -> unshiftLambda e
                    | (maxlike, l, r, LamExp(t, AppExp(AppExp(FreeVarExp(ValName op, _), e1), e2))) when maxOrMin maxlike && addOrSub op && not (findBoundVar 0 e1) ->
                        let e1 = unshiftLambda e1
                        let e2 = app3f maxlike l r (LamExp(t, e2))
                        go (app2f op e1 e2)
                    | (maxlike, l, r, LamExp(t, AppExp(AppExp(FreeVarExp(ValName op, _), e1), e2))) when maxOrMin maxlike && addOrSub op && not (findBoundVar 0 e2) ->
                        let e1 = app3f maxlike l r (LamExp(t, e1))
                        let e2 = unshiftLambda e2
                        go (app2f op e1 e2)
                    | _ -> AppExp(AppExp(AppExp(FreeVarExp(ValName f, t), x), y), z)
                | _ -> AppExp(f, go x)

            match t with
            | FunRTy(_, t3) -> (y, t3)
            | _ -> failwith "failed to construct a type"
        | IfThenElseExp(e1, e2, e3) ->
            let (e1, _) = go acc e1
            let (e2, t2) = go acc e2
            let (e3, t3) = go acc e3
            match e1 with
            | BoolExp p ->
                if p then (e2, t2)
                else (e3, t3)
            | _ -> (IfThenElseExp(e1, e2, e3), t2)
        | IntExp n -> (IntExp n, ZahlRTy)
        | BoolExp p -> (BoolExp p, BoolRTy)
    fun e ->
        let (e, _) = go [] e
        e
