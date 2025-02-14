{-# LANGUAGE FlexibleContexts #-}
{-# LANGUAGE LambdaCase #-}

-- |
-- Module      : Jikka.Core.Convert.MatrixExponentiation
-- Description : replaces repeated applications of linear (or, affine) functions with powers of matrices. / 線形な (あるいは affine な) 関数の繰り返しの適用を行列累乗で置き換えます。
-- Copyright   : (c) Kimiyuki Onaka, 2021
-- License     : Apache License 2.0
-- Maintainer  : kimiyuki95@gmail.com
-- Stability   : experimental
-- Portability : portable
module Jikka.Core.Convert.MatrixExponentiation
  ( run,
  )
where

import Control.Monad.Trans
import Control.Monad.Trans.Maybe
import qualified Data.Vector as V
import Jikka.Common.Alpha
import Jikka.Common.Error
import Jikka.Common.Matrix
import Jikka.Core.Language.ArithmeticalExpr
import Jikka.Core.Language.BuiltinPatterns
import Jikka.Core.Language.Expr
import Jikka.Core.Language.FreeVars
import Jikka.Core.Language.Lint
import Jikka.Core.Language.RewriteRules
import Jikka.Core.Language.Util

toLinearExpression :: VarName -> Expr -> Maybe (Maybe Expr, Maybe Expr)
toLinearExpression x e = do
  (a, b) <- makeVectorFromArithmeticalExpr (V.singleton x) (parseArithmeticalExpr e)
  case V.toList a of
    [a] ->
      let a' = if isOneArithmeticalExpr a then Nothing else Just (formatArithmeticalExpr a)
          b' = if isZeroArithmeticalExpr b then Nothing else Just (formatArithmeticalExpr b)
       in Just (a', b')
    _ -> error $ "Jikka.Core.Convert.MatrixExponentiation.toLinearExpression: size mismtach: " ++ show (V.length a)

fromMatrix :: Matrix ArithmeticalExpr -> Expr
fromMatrix f =
  let (h, w) = matsize f
      go row = uncurryApp (Tuple' (replicate w IntTy)) (map formatArithmeticalExpr (V.toList row))
   in uncurryApp (Tuple' (replicate h (TupleTy (replicate w IntTy)))) (map go (V.toList (unMatrix f)))

fromAffineMatrix :: Matrix ArithmeticalExpr -> V.Vector ArithmeticalExpr -> Expr
fromAffineMatrix a b | fst (matsize a) /= V.length b = error $ "Jikka.Core.Convert.MatrixExponentiation.fromAffineMatrix: size mismtach: " ++ show (matsize a) ++ " and " ++ show (V.length b)
fromAffineMatrix a b =
  let (h, w) = matsize a
      go row c = uncurryApp (Tuple' (replicate (w + 1) IntTy)) (map formatArithmeticalExpr (V.toList row ++ [c]))
      bottom = uncurryApp (Tuple' (replicate (w + 1) IntTy)) (replicate w (LitInt' 0) ++ [LitInt' 1])
   in uncurryApp (Tuple' (replicate (h + 1) (TupleTy (replicate (w + 1) IntTy)))) (V.toList (V.zipWith go (unMatrix a) b) ++ [bottom])

toMatrix :: MonadAlpha m => [(VarName, Type)] -> VarName -> Int -> Expr -> m (Maybe (Matrix ArithmeticalExpr, Maybe (V.Vector ArithmeticalExpr)))
toMatrix env x n step =
  case curryApp step of
    (Tuple' _, es) -> runMaybeT $ do
      xs <- V.fromList <$> replicateM n (lift (genVarName x))
      let unpackTuple _ e = case e of
            Proj' _ i (Var x') | x' == x -> Var (xs V.! i)
            _ -> e
      rows <- MaybeT . return . forM es $ \e -> do
        let e' = mapExpr unpackTuple env e
        guard $ x `isUnusedVar` e'
        makeVectorFromArithmeticalExpr xs (parseArithmeticalExpr e')
      a <- MaybeT . return $ makeMatrix (V.fromList (map fst rows))
      let b = if all (isZeroArithmeticalExpr . snd) rows then Nothing else Just (V.fromList (map snd rows))
      return (a, b)
    _ -> return Nothing

addOneToVector :: Int -> VarName -> Expr
addOneToVector n x =
  let ts = replicate n IntTy
   in uncurryApp (Tuple' (IntTy : ts)) (map (\i -> Proj' ts i (Var x)) [0 .. n - 1] ++ [LitInt' 1])

removeOneFromVector :: Int -> VarName -> Expr
removeOneFromVector n x =
  let ts = replicate n IntTy
   in uncurryApp (Tuple' ts) (map (\i -> Proj' (IntTy : ts) i (Var x)) [0 .. n - 1])

rule :: MonadAlpha m => RewriteRule m
rule = RewriteRule $ \env -> \case
  Iterate' IntTy k (Lam x _ step) base -> do
    let step' = toLinearExpression x step
    return $ case step' of
      Nothing -> Nothing
      Just (Nothing, Nothing) -> Just base
      Just (Nothing, Just b) -> Just $ Plus' base (Mult' k b)
      Just (Just a, Nothing) -> Just $ Mult' (Pow' a k) base
      Just (Just a, Just b) ->
        let a' = Pow' a k
            b' = Mult' (FloorDiv' (Minus' (Pow' a k) (LitInt' 1)) (Minus' a (LitInt' 1))) b -- This division has no remainder.
         in Just $ Plus' (Mult' a' base) b'
  Iterate' (TupleTy ts) k (Lam x _ step) base | isVectorTy' ts -> do
    let n = length ts
    let go n step base = MatAp' n n (MatPow' n step k) base
    step <- toMatrix env x n step
    case step of
      Nothing -> return Nothing
      Just (a, Nothing) -> return . Just $ go n (fromMatrix a) base
      Just (a, Just b) -> do
        y <- genVarName x
        z <- genVarName x
        return . Just $
          Let y (TupleTy ts) base $
            Let z (TupleTy (IntTy : ts)) (go (n + 1) (fromAffineMatrix a b) (addOneToVector n y)) $
              removeOneFromVector n z
  _ -> return Nothing

runProgram :: (MonadAlpha m, MonadError Error m) => Program -> m Program
runProgram = applyRewriteRuleProgram' rule

-- | `run` simplifies an affine functions from vectors to vectors in @iterate@ (`Iterate`) functions.
--
-- == Examples
--
-- This makes matrix multiplication. Before:
--
-- > iterate n (fun xs -> (xs[0] + 2 * xs[1], xs[1])) xs
--
-- After:
--
-- > matap (matpow ((1, 2), (0, 1)) n) xs
--
-- Also this works on integers. Before:
--
-- > iterate n (fun x -> (2 x + 1)) x
--
-- After:
--
-- > (2 ** n) * x + (2 ** n - 1) / (n - 1)
run :: (MonadAlpha m, MonadError Error m) => Program -> m Program
run prog = wrapError' "Jikka.Core.Convert.MatrixExponentiation" $ do
  precondition $ do
    ensureWellTyped prog
  prog <- runProgram prog
  postcondition $ do
    ensureWellTyped prog
  return prog
