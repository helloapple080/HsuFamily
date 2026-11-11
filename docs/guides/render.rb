# frozen_string_literal: true

require 'cgi'
require 'rdoc'
require 'rdoc/markdown'

abort "Usage: ruby docs/guides/render.rb [--check]" unless [[], ['--check']].include?(ARGV)

%w[developer user].each do |guide|
  source = File.read(File.join(__dir__, "#{guide}.md"), encoding: 'UTF-8')
  title = source.lines.first.delete_prefix('# ').strip
  options = RDoc::Options.new
  options.output_decoration = false
  body = RDoc::Markdown.new.parse(source).accept(RDoc::Markup::ToHtml.new(options))
  body = body.gsub('href="user.md"', 'href="user.html"')
  rendered = <<~HTML
    <!doctype html>
    <html lang="zh-Hant">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <title>#{CGI.escapeHTML(title)}</title>
      <style>
        :root { color-scheme: light dark; font-family: system-ui, "PingFang TC", sans-serif; }
        body { max-width: 48rem; margin: auto; padding: 2rem 1.25rem 4rem; line-height: 1.75; }
        h1, h2 { line-height: 1.3; }
        h2 { margin-top: 2rem; border-top: 1px solid GrayText; padding-top: 1rem; }
        a { text-underline-offset: .2em; }
        pre { overflow-x: auto; padding: 1rem; border: 1px solid GrayText; border-radius: .5rem; }
        code, tt { overflow-wrap: anywhere; }
        li + li { margin-top: .35rem; }
        @media print { nav { display: none; } pre { white-space: pre-wrap; } h2 { break-after: avoid; } }
      </style>
    </head>
    <body>
      <nav aria-label="手冊切換"><a href="user.html">家人使用</a> · <a href="developer.html">開發與部署</a></nav>
      <main>
    #{body.strip}
      </main>
    </body>
    </html>
  HTML
  target = File.join(__dir__, "#{guide}.html")
  if ARGV.include?('--check')
    abort "Out of date: #{target}" unless File.file?(target) && File.read(target, encoding: 'UTF-8') == rendered
  else
    File.write(target, rendered, encoding: 'UTF-8')
  end
end
