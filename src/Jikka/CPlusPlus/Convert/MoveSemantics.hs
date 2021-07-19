{-# LANGUAGE FlexibleContexts #-}
{-# LANGUAGE LambdaCase #-}

-- |
-- Module      : Jikka.CPlusPlus.Convert.MoveSemantics
-- Description : removes unnecessary copying. / 無用なコピーを削除します。
-- Copyright   : (c) Kimiyuki Onaka, 2020
-- License     : Apache License 2.0
-- Maintainer  : kimiyuki95@gmail.com
-- Stability   : experimental
-- Portability : portable
module Jikka.CPlusPlus.Convert.MoveSemantics
  ( run,
  )
where

import Control.Monad.State.Strict
import qualified Data.Map as M
import Data.Maybe
import qualified Data.Set as S
import Jikka.CPlusPlus.Language.Expr
import Jikka.CPlusPlus.Language.Util
import Jikka.Common.Error

runExpr :: MonadState (M.Map VarName VarName) m => Expr -> m Expr
runExpr = \case
  Var x -> do
    y <- gets (M.lookup x)
    return $ Var (fromMaybe x y)
  Lit lit -> return $ Lit lit
  UnOp op e -> UnOp op <$> runExpr e
  BinOp op e1 e2 -> BinOp op <$> runExpr e1 <*> runExpr e2
  Cond e1 e2 e3 -> Cond <$> runExpr e1 <*> runExpr e2 <*> runExpr e3
  Lam args ret body -> Lam args ret <$> runStatementsInLambda body
  Call e args -> Call <$> runFunction e <*> mapM runExpr args
  ArrayExt t es -> ArrayExt t <$> mapM runExpr es
  VecExt t es -> VecExt t <$> mapM runExpr es
  At e1 e2 -> At <$> runExpr e1 <*> runExpr e2
  Cast t e -> Cast t <$> runExpr e

runFunction :: MonadState (M.Map VarName VarName) m => Function -> m Function
runFunction = \case
  Callable e -> Callable <$> runExpr e
  Function h ts -> return $ Function h ts
  Method e h -> Method <$> runExpr e <*> return h
  StdTuple ts -> return $ StdTuple ts
  StdGet n -> return $ StdGet n

runLeftExpr :: MonadState (M.Map VarName VarName) m => LeftExpr -> m LeftExpr
runLeftExpr = \case
  LeftVar x -> do
    y <- gets (M.lookup x)
    return $ LeftVar (fromMaybe x y)
  LeftAt e1 e2 -> LeftAt <$> runLeftExpr e1 <*> runExpr e2
  LeftGet n e -> LeftGet n <$> runLeftExpr e

runAssignExpr :: MonadState (M.Map VarName VarName) m => AssignExpr -> m AssignExpr
runAssignExpr = \case
  AssignExpr op e1 e2 -> AssignExpr op <$> runLeftExpr e1 <*> runExpr e2
  AssignIncr e -> AssignIncr <$> runLeftExpr e
  AssignDecr e -> AssignDecr <$> runLeftExpr e

isMovable :: VarName -> [[Statement]] -> Bool
isMovable x cont = all (\stmt -> x `S.notMember` freeVarsStatement stmt) (concat cont)

runStatement :: MonadState (M.Map VarName VarName) m => Statement -> [[Statement]] -> m [Statement]
runStatement stmt cont = case stmt of
  ExprStatement e -> do
    e <- runExpr e
    return [ExprStatement e]
  Block stmts -> do
    runStatements stmts cont
  If e body1 body2 -> do
    e <- runExpr e
    body1 <- runStatements body1 cont
    body2 <- traverse (`runStatements` cont) body2
    return [If e body1 body2]
  For t x init pred incr body -> do
    init <- runExpr init
    pred <- runExpr pred
    incr <- runAssignExpr incr
    body <- runStatements body cont
    return [For t x init pred incr body]
  ForEach t x e body -> do
    e <- runExpr e
    body <- runStatements body cont
    return [ForEach t x e body]
  While e body -> do
    e <- runExpr e
    body <- runStatements body cont
    return [While e body]
  Declare t x e -> do
    e <- traverse runExpr e
    case e of
      Just (Var y) | y `isMovable` cont -> do
        modify' (M.insert x y)
        return []
      _ -> do
        return [Declare t x e]
  DeclareDestructure xs e -> do
    e <- runExpr e
    return [DeclareDestructure xs e]
  Assign e -> do
    e <- runAssignExpr e
    case e of
      AssignExpr SimpleAssign (LeftVar y) (Var x) | x == y -> return []
      _ -> return [Assign e]
  Assert e -> do
    e <- runExpr e
    return [Assert e]
  Return e -> do
    e <- runExpr e
    return [Return e]

runStatementsInLambda :: MonadState (M.Map VarName VarName) m => [Statement] -> m [Statement]
runStatementsInLambda stmts = concat <$> mapM runStatementInLambda stmts

runStatementInLambda :: MonadState (M.Map VarName VarName) m => Statement -> m [Statement]
runStatementInLambda = \case
  ExprStatement e -> do
    e <- runExpr e
    return [ExprStatement e]
  Block stmts -> do
    runStatementsInLambda stmts
  If e body1 body2 -> do
    e <- runExpr e
    body1 <- runStatementsInLambda body1
    body2 <- traverse runStatementsInLambda body2
    return [If e body1 body2]
  For t x init pred incr body -> do
    init <- runExpr init
    pred <- runExpr pred
    incr <- runAssignExpr incr
    body <- runStatementsInLambda body
    return [For t x init pred incr body]
  ForEach t x e body -> do
    e <- runExpr e
    body <- runStatementsInLambda body
    return [ForEach t x e body]
  While e body -> do
    e <- runExpr e
    body <- runStatementsInLambda body
    return [While e body]
  Declare t x e -> do
    e <- traverse runExpr e
    return [Declare t x e]
  DeclareDestructure xs e -> do
    e <- runExpr e
    return [DeclareDestructure xs e]
  Assign e -> do
    e <- runAssignExpr e
    case e of
      AssignExpr SimpleAssign (LeftVar y) (Var x) | x == y -> return []
      _ -> return [Assign e]
  Assert e -> do
    e <- runExpr e
    return [Assert e]
  Return e -> do
    e <- runExpr e
    return [Return e]

runStatements :: MonadState (M.Map VarName VarName) m => [Statement] -> [[Statement]] -> m [Statement]
runStatements stmts cont = case stmts of
  [] -> return []
  stmt : stmts -> do
    stmt <- runStatement stmt (stmts : cont)
    stmts <- runStatements stmts cont
    return (stmt ++ stmts)

runToplevelStatement :: MonadState (M.Map VarName VarName) m => ToplevelStatement -> m ToplevelStatement
runToplevelStatement = \case
  VarDef t x e -> VarDef t x <$> runExpr e
  FunDef ret f args body -> FunDef ret f args <$> runStatements body []

runProgram :: Monad m => Program -> m Program
runProgram (Program decls) = (`evalStateT` M.empty) $ do
  Program <$> mapM runToplevelStatement decls

-- | `run` replaces superfluous copying.
--
-- == Examples
--
-- Before:
--
-- > vector<int> solve(vector<int> a) {
-- >     vector<int> b = a;
-- >     b[0] = 1;
-- >     return b;
-- > }
--
-- After:
--
-- > vector<int> solve(vector<int> a) {
-- >     a[0] = 1;
-- >     return a;
-- > }
run :: MonadError Error m => Program -> m Program
run prog = wrapError' "Jikka.CPlusPlus.Convert.MoveSemantics" $ do
  runProgram prog
