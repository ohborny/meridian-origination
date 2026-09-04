using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using MortgageLOS;

namespace MortgageLOS.Origination
{
    // =========================================================================
    // LoanProductService - Product lookup service
    // Extracted from LoanApplicationService in 2015 to "improve separation
    // of concerns." The original lookup code is still in LoanApplicationService.
    // =========================================================================

    public class LoanProductService
    {
        private DatabaseHelper _db;

        public LoanProductService()
        {
            _db = new DatabaseHelper();
        }

        public LoanProduct GetProduct(string productCode)
        {
            if (string.IsNullOrEmpty(productCode))
                return null;

            string sql = "SELECT * FROM loan_products WHERE product_code = @code";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@code", productCode, DbType.String));

            if (dt.Rows.Count == 0)
                return null;

            return MapProductFromRow(dt.Rows[0]);
        }

        public LoanProduct GetProductById(int productId)
        {
            string sql = "SELECT * FROM loan_products WHERE product_id = @id";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@id", productId, DbType.Int32));

            if (dt.Rows.Count == 0)
                return null;

            return MapProductFromRow(dt.Rows[0]);
        }

        public List<LoanProduct> ListActiveProducts()
        {
            string sql = "SELECT * FROM loan_products WHERE is_active = true ORDER BY product_code";
            DataTable dt = _db.ExecuteCoreQuery(sql);

            List<LoanProduct> products = new List<LoanProduct>();
            foreach (DataRow row in dt.Rows)
            {
                products.Add(MapProductFromRow(row));
            }
            return products;
        }

        public List<LoanProduct> GetProductsByType(string productType)
        {
            string sql = "SELECT * FROM loan_products WHERE product_type = @type AND is_active = true ORDER BY product_code";
            DataTable dt = _db.ExecuteCoreQuery(sql, _db.CreateParam("@type", productType, DbType.String));

            List<LoanProduct> products = new List<LoanProduct>();
            foreach (DataRow row in dt.Rows)
            {
                products.Add(MapProductFromRow(row));
            }
            return products;
        }

        private LoanProduct MapProductFromRow(DataRow row)
        {
            LoanProduct product = new LoanProduct();
            product.ProductId = Convert.ToInt32(row["product_id"]);
            product.ProductCode = row["product_code"]?.ToString();
            product.ProductName = row["product_name"]?.ToString();
            product.ProductType = row["product_type"]?.ToString();
            product.TermMonths = Convert.ToInt32(row["term_months"]);
            product.AmortizationType = row["amortization_type"]?.ToString();
            product.MinLoanAmt = row["min_loan_amt"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["min_loan_amt"]);
            product.MaxLoanAmt = row["max_loan_amt"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_loan_amt"]);
            product.MinFico = row["min_fico"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["min_fico"]);
            product.MaxLtv = row["max_ltv"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_ltv"]);
            product.MaxDti = row["max_dti"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(row["max_dti"]);
            product.IsActive = Convert.ToBoolean(row["is_active"]);
            product.InvestorCode = row["investor_code"] == DBNull.Value ? null : row["investor_code"].ToString();
            return product;
        }
    }
}
